using System.Text.Json;
using System.Transactions;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Repositories;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Transaction 2 của đặt sân online (spec 7.6, P6-14): mỗi nhánh mở đầu bằng câu cổng status = PENDING rồi ghi hóa đơn,
/// slot lock, sân của đơn. Chạy trên schema thật: các CHECK (chk_bookings_pending_approval, chk_invoices_deposit_fields,
/// chk_bookings_cancel_reason, chk_bookings_cancelled_at) là lưới an toàn cuối.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BookingDepositUpdateTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    private static readonly DateTime ApprovalExpiresAt = CreatedAt.AddMinutes(30);

    public static TheoryData<BookingStatus> NotPendingStatuses => new()
    {
        BookingStatus.CANCELLED, BookingStatus.CONFIRMED, BookingStatus.PENDING_APPROVAL, BookingStatus.PAID_ONLINE
    };

    /// <summary>Chụp mọi cột mà transaction 2 có thể đổi (booking, hóa đơn, slot lock, dòng sân) để chứng minh "không đổi gì".</summary>
    private async Task<string> CaptureAsync(Guid bookingId)
    {
        var b = await ReloadBookingAsync(bookingId);
        var i = await ReloadInvoiceAsync(bookingId);
        return JsonSerializer.Serialize(new
        {
            b.Status, b.TadssDecisionId, b.UpdatedAt, b.ApprovalExpiresAt, b.ApprovalReasonCode, b.ApprovalReasons,
            b.CancelledAt, b.CancelSource, b.CancelReason, b.CancelledBy,
            i.DepositRate, i.DepositAmount, i.DepositSource, i.ExpiresAt, i.HoldDeadline, i.PaymentTiming, i.PaymentStatus, InvoiceUpdated = i.UpdatedAt,
            Locks = (await ReloadLocksAsync(bookingId)).OrderBy(l => l.CourtId).Select(l => new { l.CourtId, l.ExpiresAt }),
            Courts = (await ReloadCourtsAsync(bookingId)).OrderBy(c => c.CourtId).Select(c => new { c.CourtId, c.IsActive })
        });
    }

    // ───────────────────────── Chốt chặn transaction ─────────────────────────

    public static TheoryData<string> BranchMethods => new() { "a-d", "a-prime", "b", "c" };

    private static Task<bool> CallBranchAsync(string branch, BookingRepository repository, Guid bookingId) => branch switch
    {
        "a-d" => repository.ApplyPaymentRequiredAsync(bookingId, null, 1m, 1m, DepositSource.FULL_PAYMENT_FALLBACK, Now),
        "a-prime" => repository.ConfirmWithoutOnlinePaymentAsync(bookingId, null, DepositSource.FULL_PAYMENT_FALLBACK, Now),
        "b" => repository.HoldForApprovalAsync(bookingId, Guid.NewGuid(), ApprovalExpiresAt, "X", ["y"], Now),
        "c" => repository.RejectByPolicyAsync(bookingId, Guid.NewGuid(), Now),
        _ => throw new ArgumentOutOfRangeException(nameof(branch))
    };

    [Theory]
    [MemberData(nameof(BranchMethods))]
    public async Task BranchMethods_WhenNotInsideAnyTransaction_ThrowBeforeWritingAnything(string branch)
    {
        // Context riêng, không mở transaction nào: ghi nhiều bảng sau câu cổng mà không có transaction sẽ để lại đơn nửa vời
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => CallBranchAsync(branch, repository, Guid.NewGuid()));
    }

    [Theory]
    [MemberData(nameof(BranchMethods))]
    public async Task BranchMethods_WhenInsideTransactionScope_RunNormally(string branch)
    {
        // Đường chạy thật của BookingService dùng TransactionScope (không phải BeginTransaction của EF)
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        using var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var won = await CallBranchAsync(branch, repository, Guid.NewGuid()); // đơn không tồn tại ⇒ thua cổng, nhưng không ném

        Assert.False(won);
    }

    // ───────────────────────── ApplyPaymentRequiredAsync (nhánh a, d) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-02")]
    public async Task ApplyPaymentRequiredAsync_BranchA_StoresTadssDepositAndKeepsHoldAndLocks()
    {
        var order = await SeedOrderAsync(courtCount: 2);
        var decisionId = Guid.NewGuid();

        var won = await Repository.ApplyPaymentRequiredAsync(order.Booking.Id, decisionId, 0.2m, 40_000m, DepositSource.TADSS, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(decisionId, booking.TadssDecisionId);
        Assert.Equal(Now, booking.UpdatedAt);
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(0.2m, invoice.DepositRate);
        Assert.Equal(40_000m, invoice.DepositAmount);
        Assert.Equal(DepositSource.TADSS, invoice.DepositSource);
        Assert.Equal(PaymentTiming.PREPAID, invoice.PaymentTiming);
        Assert.Equal(InvoicePaymentStatus.UNPAID, invoice.PaymentStatus);
        // H-01: hạn của transaction 1 không đổi (expires = tạo + 10', hold_deadline = tạo + 15')
        Assert.Equal(CreatedAt.AddMinutes(10), invoice.ExpiresAt);
        Assert.Equal(CreatedAt.AddMinutes(15), invoice.HoldDeadline);
        var locks = await ReloadLocksAsync(order.Booking.Id);
        Assert.Equal(2, locks.Count);
        Assert.All(locks, l => Assert.Equal(invoice.ExpiresAt, l.ExpiresAt));
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.True(c.IsActive));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task ApplyPaymentRequiredAsync_BranchD_StoresFullPaymentFallbackWithoutDecisionId()
    {
        var order = await SeedOrderAsync(finalTotal: 150_000m);

        var won = await Repository.ApplyPaymentRequiredAsync(order.Booking.Id, null, 1m, 150_000m, DepositSource.FULL_PAYMENT_FALLBACK, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Null(booking.TadssDecisionId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(1m, invoice.DepositRate);
        Assert.Equal(150_000m, invoice.DepositAmount);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, invoice.DepositSource);
        Assert.Equal(CreatedAt.AddMinutes(15), invoice.HoldDeadline);
    }

    [Theory]
    [MemberData(nameof(NotPendingStatuses))]
    [Trait("Catalog", "BE-IT-02")]
    public async Task ApplyPaymentRequiredAsync_WhenBookingIsNotPending_ReturnsFalseAndChangesNothing(BookingStatus current)
    {
        var order = await SeedOrderAsync(current, courtCount: 2);
        var before = await CaptureAsync(order.Booking.Id);

        var won = await Repository.ApplyPaymentRequiredAsync(order.Booking.Id, Guid.NewGuid(), 0.2m, 40_000m, DepositSource.TADSS, Now);

        Assert.False(won);
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    [Fact]
    public async Task ApplyPaymentRequiredAsync_WhenBookingDoesNotExist_ReturnsFalse()
    {
        Assert.False(await Repository.ApplyPaymentRequiredAsync(Guid.NewGuid(), null, 1m, 1m, DepositSource.FULL_PAYMENT_FALLBACK, Now));
    }

    [Fact]
    public async Task ApplyPaymentRequiredAsync_WhenInvoiceIsMissing_Throws()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, CreatedAt);
        DbContext.Bookings.Add(booking);
        await DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Repository.ApplyPaymentRequiredAsync(booking.Id, null, 1m, 1m, DepositSource.FULL_PAYMENT_FALLBACK, Now));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task ApplyPaymentRequiredAsync_WhenTimeIsNotUtc_ThrowsAndChangesNothing(DateTimeKind kind)
    {
        var order = await SeedOrderAsync();
        var before = await CaptureAsync(order.Booking.Id);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repository.ApplyPaymentRequiredAsync(order.Booking.Id, null, 1m, 1m, DepositSource.FULL_PAYMENT_FALLBACK, DateTime.SpecifyKind(Now, kind)));
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    // ───────────────────────── ConfirmWithoutOnlinePaymentAsync (nhánh a′) ─────────────────────────

    [Theory]
    [InlineData(DepositSource.TADSS, true)]
    [InlineData(DepositSource.FULL_PAYMENT_FALLBACK, false)]
    [Trait("Catalog", "BE-IT-24")]
    public async Task ConfirmWithoutOnlinePaymentAsync_ConfirmsWithAppliedDepositZeroPostpaidAndReleasesLocks(DepositSource source, bool hasDecision)
    {
        var order = await SeedOrderAsync(courtCount: 2);
        Guid? decisionId = hasDecision ? Guid.NewGuid() : null;

        var won = await Repository.ConfirmWithoutOnlinePaymentAsync(order.Booking.Id, decisionId, source, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        Assert.Equal(decisionId, booking.TadssDecisionId);
        Assert.Equal(Now, booking.UpdatedAt);
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(0m, invoice.DepositRate);
        Assert.Equal(0m, invoice.DepositAmount);
        Assert.Equal(source, invoice.DepositSource);
        Assert.Equal(PaymentTiming.POSTPAID, invoice.PaymentTiming);
        Assert.Null(invoice.ExpiresAt);
        Assert.Equal(CreatedAt.AddMinutes(15), invoice.HoldDeadline); // H-01: hold_deadline giữ nguyên
        Assert.Equal(InvoicePaymentStatus.UNPAID, invoice.PaymentStatus); // thu toàn bộ tại quầy
        Assert.Empty(await ReloadLocksAsync(order.Booking.Id));
        // Sân vẫn được giữ qua booking_courts
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task ConfirmWithoutOnlinePaymentAsync_DoesNotTouchOtherBookingsLocks()
    {
        var order = await SeedOrderAsync();
        var other = await SeedOrderAsync(seed: order.Seed, playDate: PlayDate.AddDays(1));

        await Repository.ConfirmWithoutOnlinePaymentAsync(order.Booking.Id, null, DepositSource.FULL_PAYMENT_FALLBACK, Now);

        Assert.Single(await ReloadLocksAsync(other.Booking.Id));
        Assert.Equal(BookingStatus.PENDING, (await ReloadBookingAsync(other.Booking.Id)).Status);
        Assert.Equal(PaymentTiming.PREPAID, (await ReloadInvoiceAsync(other.Booking.Id)).PaymentTiming);
    }

    [Theory]
    [MemberData(nameof(NotPendingStatuses))]
    [Trait("Catalog", "BE-IT-24")]
    public async Task ConfirmWithoutOnlinePaymentAsync_WhenBookingIsNotPending_ReturnsFalseAndKeepsLocksAndInvoice(BookingStatus current)
    {
        var order = await SeedOrderAsync(current, courtCount: 2);
        var before = await CaptureAsync(order.Booking.Id);

        var won = await Repository.ConfirmWithoutOnlinePaymentAsync(order.Booking.Id, Guid.NewGuid(), DepositSource.TADSS, Now);

        Assert.False(won);
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
        Assert.Equal(2, (await ReloadLocksAsync(order.Booking.Id)).Count);
    }

    // ───────────────────────── HoldForApprovalAsync (nhánh b) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-03")]
    public async Task HoldForApprovalAsync_MovesToPendingApprovalAndExtendsLocksToApprovalDeadline()
    {
        var order = await SeedOrderAsync(courtCount: 2);
        var decisionId = Guid.NewGuid();
        var reasons = new[] { "Giá trị đơn 2.500.000 VND đạt ngưỡng cần duyệt.", "Khách mới" };

        var won = await Repository.HoldForApprovalAsync(order.Booking.Id, decisionId, ApprovalExpiresAt, "HIGH_VALUE_REVIEW", reasons, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.PENDING_APPROVAL, booking.Status);
        Assert.Equal(decisionId, booking.TadssDecisionId);
        Assert.Equal(ApprovalExpiresAt, booking.ApprovalExpiresAt);
        Assert.Equal(DateTimeKind.Utc, booking.ApprovalExpiresAt!.Value.Kind);
        Assert.Equal("HIGH_VALUE_REVIEW", booking.ApprovalReasonCode);
        Assert.Equal(reasons, booking.ApprovalReasons);
        Assert.Null(booking.ApprovalResult);
        Assert.Null(booking.ApprovalResolvedAt);
        Assert.Equal(Now, booking.UpdatedAt);

        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Null(invoice.ExpiresAt);
        Assert.Null(invoice.DepositRate);
        Assert.Null(invoice.DepositAmount);
        Assert.Null(invoice.DepositSource);
        Assert.Equal(InvoicePaymentStatus.UNPAID, invoice.PaymentStatus);
        Assert.Equal(PaymentTiming.PREPAID, invoice.PaymentTiming);
        // H-02: hold_deadline giữ nguyên; 6A-3 ghi đè khi duyệt
        Assert.Equal(CreatedAt.AddMinutes(15), invoice.HoldDeadline);

        var locks = await ReloadLocksAsync(order.Booking.Id);
        Assert.Equal(2, locks.Count);
        Assert.All(locks, l => Assert.Equal(ApprovalExpiresAt, l.ExpiresAt));
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.True(c.IsActive));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-03")]
    public async Task HoldForApprovalAsync_WhenNoReasons_StoresEmptyArray()
    {
        var order = await SeedOrderAsync();

        await Repository.HoldForApprovalAsync(order.Booking.Id, Guid.NewGuid(), ApprovalExpiresAt, "NO_SHOW_HISTORY_REVIEW", [], Now);

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.NotNull(booking.ApprovalReasons);
        Assert.Empty(booking.ApprovalReasons!);
    }

    [Fact]
    public async Task HoldForApprovalAsync_DoesNotTouchOtherBookingsLocks()
    {
        var order = await SeedOrderAsync();
        var other = await SeedOrderAsync(seed: order.Seed, playDate: PlayDate.AddDays(1));

        await Repository.HoldForApprovalAsync(order.Booking.Id, Guid.NewGuid(), ApprovalExpiresAt, "X", ["y"], Now);

        Assert.All(await ReloadLocksAsync(other.Booking.Id), l => Assert.Equal(CreatedAt.AddMinutes(10), l.ExpiresAt));
        Assert.Equal(BookingStatus.PENDING, (await ReloadBookingAsync(other.Booking.Id)).Status);
    }

    [Theory]
    [MemberData(nameof(NotPendingStatuses))]
    [Trait("Catalog", "BE-IT-03")]
    public async Task HoldForApprovalAsync_WhenBookingIsNotPending_ReturnsFalseAndChangesNothing(BookingStatus current)
    {
        var order = await SeedOrderAsync(current, courtCount: 2);
        var before = await CaptureAsync(order.Booking.Id);

        var won = await Repository.HoldForApprovalAsync(order.Booking.Id, Guid.NewGuid(), ApprovalExpiresAt, "X", ["y"], Now);

        Assert.False(won);
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    [Fact]
    public async Task HoldForApprovalAsync_WhenApprovalDeadlineIsNotUtc_ThrowsAndChangesNothing()
    {
        var order = await SeedOrderAsync();
        var before = await CaptureAsync(order.Booking.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => Repository.HoldForApprovalAsync(
            order.Booking.Id, Guid.NewGuid(), DateTime.SpecifyKind(ApprovalExpiresAt, DateTimeKind.Unspecified), "X", ["y"], Now));
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    // ───────────────────────── RejectByPolicyAsync (nhánh c) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-17")]
    public async Task RejectByPolicyAsync_CancelsOrderAndReleasesCourtsLocksAndInvoice()
    {
        var order = await SeedOrderAsync(courtCount: 2);
        var decisionId = Guid.NewGuid();

        var won = await Repository.RejectByPolicyAsync(order.Booking.Id, decisionId, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(CancelSourceEnum.SYSTEM, booking.CancelSource);
        Assert.Equal(BookingCancelReason.POLICY_REJECTED, booking.CancelReason);
        Assert.Equal(Now, booking.CancelledAt);
        Assert.Null(booking.CancelledBy);
        Assert.Equal(decisionId, booking.TadssDecisionId);
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.False(c.IsActive));
        Assert.Empty(await ReloadLocksAsync(order.Booking.Id));
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, invoice.PaymentStatus);
        Assert.Equal(CreatedAt.AddMinutes(15), invoice.HoldDeadline);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-17")]
    public async Task RejectByPolicyAsync_FreesTheSlotSoAnotherBookingCanTakeTheSameCourtAndTime()
    {
        var order = await SeedOrderAsync();
        await Repository.RejectByPolicyAsync(order.Booking.Id, Guid.NewGuid(), Now);

        var customer = await Seeder.SeedCustomerAsync();
        var next = TestDataSeeder.NewBooking(order.Seed.Branch.Id, customer.Id, BookingStatus.PENDING, Now);
        DbContext.Bookings.Add(next);
        DbContext.Invoices.Add(TestDataSeeder.NewInvoice(next));
        await DbContext.SaveChangesAsync();
        // excl_booking_courts_no_overlap và excl_slot_locks_no_overlap không còn chặn cùng sân, ngày, giờ
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(next.Id, order.Courts[0].Id, PlayDate, Start, End));
        DbContext.SlotLocks.Add(TestDataSeeder.NewSlotLock(next.Id, order.Courts[0].Id, PlayDate, Start, End, Now.AddMinutes(10)));

        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task RejectByPolicyAsync_DoesNotTouchOtherBookings()
    {
        var order = await SeedOrderAsync();
        var other = await SeedOrderAsync(seed: order.Seed, playDate: PlayDate.AddDays(1));
        var before = await CaptureAsync(other.Booking.Id);

        await Repository.RejectByPolicyAsync(order.Booking.Id, Guid.NewGuid(), Now);

        Assert.Equal(before, await CaptureAsync(other.Booking.Id));
    }

    [Theory]
    [MemberData(nameof(NotPendingStatuses))]
    [Trait("Catalog", "BE-IT-17")]
    public async Task RejectByPolicyAsync_WhenBookingIsNotPending_ReturnsFalseAndChangesNothing(BookingStatus current)
    {
        var order = await SeedOrderAsync(current, courtCount: 2);
        var before = await CaptureAsync(order.Booking.Id);

        var won = await Repository.RejectByPolicyAsync(order.Booking.Id, Guid.NewGuid(), Now);

        Assert.False(won);
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.True(c.IsActive));
        Assert.Equal(2, (await ReloadLocksAsync(order.Booking.Id)).Count);
    }

    [Fact]
    public async Task RejectByPolicyAsync_WhenStaffCancelledFirst_DoesNotOverwriteStaffCancellation()
    {
        var order = await SeedOrderAsync();
        await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.STAFF, BookingCancelReason.STAFF_CANCELLED, null, Now);

        var won = await Repository.RejectByPolicyAsync(order.Booking.Id, Guid.NewGuid(), Now.AddSeconds(1));

        Assert.False(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(CancelSourceEnum.STAFF, booking.CancelSource);
        Assert.Equal(BookingCancelReason.STAFF_CANCELLED, booking.CancelReason);
        Assert.Null(booking.TadssDecisionId);
    }

    [Fact]
    public async Task RejectByPolicyAsync_WhenCalledTwice_SecondCallLosesTheGate()
    {
        var order = await SeedOrderAsync();
        var firstDecision = Guid.NewGuid();

        Assert.True(await Repository.RejectByPolicyAsync(order.Booking.Id, firstDecision, Now));
        Assert.False(await Repository.RejectByPolicyAsync(order.Booking.Id, Guid.NewGuid(), Now.AddSeconds(1)));

        Assert.Equal(firstDecision, (await ReloadBookingAsync(order.Booking.Id)).TadssDecisionId);
    }
}
