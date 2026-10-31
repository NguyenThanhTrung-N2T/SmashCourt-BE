using SmashCourt_BE.Models.Deposit;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Phase 6A-3 (spec 7.7), trên schema thật: hàng chờ duyệt và ba câu ghi có điều kiện của duyệt / từ chối. Câu cổng chỉ thắng khi đơn
/// còn PENDING_APPROVAL và còn hạn duyệt (không bao giờ duyệt đơn quá hạn, kể cả khi job hết hạn chưa chạy).
/// </summary>
[Trait("Category", "Integration")]
public sealed class BookingApprovalRepositoryTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    private static readonly DateTime ApprovalExpiresAt = Now.AddMinutes(28);
    private static readonly PaymentHoldWindow Window = new(Now.AddMinutes(15), Now.AddMinutes(15));
    private static readonly DateTime TokenExpiresAt = Now.AddHours(20);
    private static readonly string TokenHash = CancelTokenHelper.Hash("approval-raw-token");
    private const string Note = "Khách quen, đã gọi xác nhận";

    public static TheoryData<BookingStatus> NotPendingApproval => new()
    {
        BookingStatus.PENDING, BookingStatus.CONFIRMED, BookingStatus.CANCELLED, BookingStatus.PAID_ONLINE
    };

    /// <summary>Đơn như transaction 2 nhánh b để lại: hóa đơn chưa cọc, không hạn thanh toán; slot lock giữ tới hạn duyệt.</summary>
    private async Task<PendingOrder> SeedPendingApprovalAsync(
        int courtCount = 1, BranchSeedData? seed = null, DateTime? approvalExpiresAt = null, DateOnly? playDate = null,
        BookingStatus status = BookingStatus.PENDING_APPROVAL)
    {
        var order = await SeedOrderAsync(status, courtCount, seed, playDate: playDate);
        var expires = approvalExpiresAt ?? ApprovalExpiresAt;
        // Đơn từng chờ duyệt giữ hạn duyệt sau khi rời hàng chờ (để tra cứu): truyền hạn thì đơn ở trạng thái khác cũng có hạn
        if (status == BookingStatus.PENDING_APPROVAL || approvalExpiresAt.HasValue)
        {
            order.Booking.ApprovalExpiresAt = expires;
            order.Booking.ApprovalReasonCode = "LARGE_ORDER_REVIEW";
            order.Booking.ApprovalReasons = ["Đơn từ 2.000.000 đ cần duyệt."];
        }
        order.Invoice.ExpiresAt = null;
        foreach (var slotLock in order.Locks)
            slotLock.ExpiresAt = expires;
        await DbContext.SaveChangesAsync();
        return order;
    }

    private Task<User> SeedManagerAsync() => Seeder.SeedCustomerAsync(); // chỉ cần một user có thật cho khóa ngoại approval_resolved_by

    private async Task<string> CaptureAsync(Guid bookingId)
    {
        var b = await ReloadBookingAsync(bookingId);
        var i = await ReloadInvoiceAsync(bookingId);
        return JsonSerializer.Serialize(new
        {
            b.Status, b.UpdatedAt, b.ApprovalResult, b.ApprovalResolvedBy, b.ApprovalResolvedAt, b.ApprovalResolutionNote,
            b.CancelledAt, b.CancelSource, b.CancelReason, b.CancelledBy, b.CancelTokenHash, b.CancelTokenExpiresAt,
            i.DepositRate, i.DepositAmount, i.DepositSource, i.ExpiresAt, i.HoldDeadline, i.PaymentTiming, i.PaymentStatus, InvoiceUpdated = i.UpdatedAt,
            Locks = (await ReloadLocksAsync(bookingId)).OrderBy(l => l.CourtId).Select(l => new { l.CourtId, l.ExpiresAt }),
            Courts = (await ReloadCourtsAsync(bookingId)).OrderBy(c => c.CourtId).Select(c => new { c.CourtId, c.IsActive })
        });
    }

    // ───────────────────────── Chốt chặn ─────────────────────────

    public static TheoryData<string> Methods => new() { "approve", "approve-zero", "reject" };

    private static Task<bool> CallAsync(string method, BookingRepository repository, Guid bookingId, DateTime now) => method switch
    {
        "approve" => repository.ApproveWithDepositAsync(bookingId, null, Note, 0.3m, 60_000m, Window, now),
        "approve-zero" => repository.ApproveWithoutDepositAsync(bookingId, null, Note, TokenHash, TokenExpiresAt, now),
        "reject" => repository.RejectByManagerAsync(bookingId, null, Note, now),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Methods_WhenNotInsideAnyTransaction_ThrowBeforeWritingAnything(string method)
    {
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => CallAsync(method, repository, Guid.NewGuid(), Now));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Methods_WhenNowIsNotUtc_Throw(string method)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CallAsync(method, Repository, Guid.NewGuid(), DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    [Trait("Catalog", "BE-IT-15")]
    public async Task Methods_WhenApprovalExpiresExactlyNow_LoseAndChangeNothing(string method)
    {
        var order = await SeedPendingApprovalAsync(approvalExpiresAt: Now);
        var before = await CaptureAsync(order.Booking.Id);

        Assert.False(await CallAsync(method, Repository, order.Booking.Id, Now));
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    [Trait("Catalog", "BE-IT-15")]
    public async Task Methods_WhenApprovalAlreadyExpired_LoseAndChangeNothing(string method)
    {
        var order = await SeedPendingApprovalAsync(approvalExpiresAt: Now.AddSeconds(-1));
        var before = await CaptureAsync(order.Booking.Id);

        Assert.False(await CallAsync(method, Repository, order.Booking.Id, Now));
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    [Trait("Catalog", "BE-IT-15")]
    public async Task Methods_WhenApprovalExpiresOneSecondLater_Win(string method)
    {
        var order = await SeedPendingApprovalAsync(approvalExpiresAt: Now.AddSeconds(1));

        Assert.True(await CallAsync(method, Repository, order.Booking.Id, Now));
    }

    public static TheoryData<string, BookingStatus> MethodsAndNotPendingStatuses()
    {
        var data = new TheoryData<string, BookingStatus>();
        foreach (var method in new[] { "approve", "approve-zero", "reject" })
            foreach (var status in new[] { BookingStatus.PENDING, BookingStatus.CONFIRMED, BookingStatus.CANCELLED, BookingStatus.PAID_ONLINE })
                data.Add(method, status);
        return data;
    }

    [Theory]
    [MemberData(nameof(MethodsAndNotPendingStatuses))]
    [Trait("Catalog", "BE-IT-15")]
    public async Task Methods_WhenBookingIsNotPendingApproval_LoseAndChangeNothing(string method, BookingStatus status)
    {
        var order = await SeedPendingApprovalAsync(status: status);
        var before = await CaptureAsync(order.Booking.Id);

        Assert.False(await CallAsync(method, Repository, order.Booking.Id, Now));
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    [Trait("Catalog", "BE-IT-13")]
    public async Task Methods_SecondCallAfterAWin_Loses(string method)
    {
        var order = await SeedPendingApprovalAsync();
        Assert.True(await CallAsync(method, Repository, order.Booking.Id, Now));
        var after = await CaptureAsync(order.Booking.Id);

        Assert.False(await CallAsync("approve", Repository, order.Booking.Id, Now.AddSeconds(1)));
        Assert.False(await CallAsync("reject", Repository, order.Booking.Id, Now.AddSeconds(1)));
        Assert.Equal(after, await CaptureAsync(order.Booking.Id));
    }

    // ───────────────────────── Duyệt có cọc ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task ApproveWithDeposit_MovesToPendingWithManagerDepositAndNewHoldWindow()
    {
        var manager = await SeedManagerAsync();
        var order = await SeedPendingApprovalAsync(courtCount: 2);

        var won = await Repository.ApproveWithDepositAsync(order.Booking.Id, manager.Id, Note, 0.3m, 60_000m, Window, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(ApprovalResult.APPROVED, booking.ApprovalResult);
        Assert.Equal(manager.Id, booking.ApprovalResolvedBy);
        Assert.Equal(Now, booking.ApprovalResolvedAt);
        Assert.Equal(Note, booking.ApprovalResolutionNote);
        Assert.Equal(Now, booking.UpdatedAt);
        Assert.Equal(ApprovalExpiresAt, booking.ApprovalExpiresAt); // giữ để tra cứu
        Assert.Null(booking.CancelTokenHash);
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(0.3m, invoice.DepositRate);
        Assert.Equal(60_000m, invoice.DepositAmount);
        Assert.Equal(DepositSource.MANAGER, invoice.DepositSource);
        Assert.Equal(Window.ExpiresAt, invoice.ExpiresAt);
        Assert.Equal(Window.HoldDeadline, invoice.HoldDeadline); // H-03: ghi đè hạn giữ chỗ lúc đặt (CreatedAt + 15')
        Assert.NotEqual(CreatedAt.AddMinutes(15), invoice.HoldDeadline);
        Assert.Equal(PaymentTiming.PREPAID, invoice.PaymentTiming);
        Assert.Equal(InvoicePaymentStatus.UNPAID, invoice.PaymentStatus);
        var locks = await ReloadLocksAsync(order.Booking.Id);
        Assert.Equal(2, locks.Count);
        Assert.All(locks, l => Assert.Equal(Window.ExpiresAt, l.ExpiresAt));
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.True(c.IsActive));
        Assert.Empty(await DbContext.Payments.AsNoTracking().Where(p => p.InvoiceId == invoice.Id).ToListAsync()); // P6-10
    }

    [Fact]
    public async Task ApproveWithDeposit_WithoutKnownResolver_LeavesResolvedByEmpty()
    {
        // Đồng bộ Q9 khi người duyệt bên TADSS không phải user của BE (chốt 24/10, phương án A)
        var order = await SeedPendingApprovalAsync();

        Assert.True(await Repository.ApproveWithDepositAsync(order.Booking.Id, null, Note, 0.5m, 100_000m, Window, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Null(booking.ApprovalResolvedBy);
        Assert.Equal(ApprovalResult.APPROVED, booking.ApprovalResult);
        Assert.Equal(Now, booking.ApprovalResolvedAt);
    }

    [Fact]
    public async Task ApproveWithDeposit_WindowNotUtc_ThrowsBeforeWriting()
    {
        var order = await SeedPendingApprovalAsync();
        var before = await CaptureAsync(order.Booking.Id);
        var local = new PaymentHoldWindow(DateTime.SpecifyKind(Window.ExpiresAt, DateTimeKind.Local), Window.HoldDeadline);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repository.ApproveWithDepositAsync(order.Booking.Id, null, Note, 0.3m, 60_000m, local, Now));
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    // ───────────────────────── Duyệt 0 % ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task ApproveWithoutDeposit_ConfirmsPostpaidWithCancelTokenAndDropsLocks()
    {
        var manager = await SeedManagerAsync();
        var order = await SeedPendingApprovalAsync(courtCount: 2);

        var won = await Repository.ApproveWithoutDepositAsync(order.Booking.Id, manager.Id, Note, TokenHash, TokenExpiresAt, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        Assert.Equal(ApprovalResult.APPROVED, booking.ApprovalResult);
        Assert.Equal(manager.Id, booking.ApprovalResolvedBy);
        Assert.Equal(Now, booking.ApprovalResolvedAt);
        Assert.Equal(Note, booking.ApprovalResolutionNote);
        Assert.Equal(TokenHash, booking.CancelTokenHash);
        Assert.Equal(TokenExpiresAt, booking.CancelTokenExpiresAt);
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(0m, invoice.DepositRate);
        Assert.Equal(0m, invoice.DepositAmount);
        Assert.Equal(DepositSource.MANAGER, invoice.DepositSource);
        Assert.Equal(PaymentTiming.POSTPAID, invoice.PaymentTiming);
        Assert.Null(invoice.ExpiresAt);
        Assert.Equal(CreatedAt.AddMinutes(15), invoice.HoldDeadline); // không đổi
        Assert.Equal(InvoicePaymentStatus.UNPAID, invoice.PaymentStatus);
        Assert.Empty(await ReloadLocksAsync(order.Booking.Id));
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task ApproveWithoutDeposit_TokenLooksUpTheBooking()
    {
        var order = await SeedPendingApprovalAsync();
        await Repository.ApproveWithoutDepositAsync(order.Booking.Id, null, Note, TokenHash, TokenExpiresAt, Now);
        DbContext.ChangeTracker.Clear();

        var found = await Repository.GetByCancelTokenAsync(CancelTokenHelper.Hash("approval-raw-token"));

        Assert.Equal(order.Booking.Id, found?.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApproveWithoutDeposit_EmptyTokenHash_ThrowsBeforeWriting(string hash)
    {
        var order = await SeedPendingApprovalAsync();
        var before = await CaptureAsync(order.Booking.Id);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repository.ApproveWithoutDepositAsync(order.Booking.Id, null, Note, hash, TokenExpiresAt, Now));
        Assert.Equal(before, await CaptureAsync(order.Booking.Id));
    }

    // ───────────────────────── Từ chối ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-06")]
    public async Task Reject_CancelsWithManagerReasonAndReleasesEverything()
    {
        var manager = await SeedManagerAsync();
        var order = await SeedPendingApprovalAsync(courtCount: 2);

        var won = await Repository.RejectByManagerAsync(order.Booking.Id, manager.Id, Note, Now);

        Assert.True(won);
        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(CancelSourceEnum.STAFF, booking.CancelSource);
        Assert.Equal(BookingCancelReason.APPROVAL_REJECTED, booking.CancelReason);
        Assert.Equal(manager.Id, booking.CancelledBy);
        Assert.Equal(Now, booking.CancelledAt);
        Assert.Equal(ApprovalResult.REJECTED, booking.ApprovalResult);
        Assert.Equal(manager.Id, booking.ApprovalResolvedBy);
        Assert.Equal(Now, booking.ApprovalResolvedAt);
        Assert.Equal(Note, booking.ApprovalResolutionNote);
        Assert.Equal(order.Booking.TadssDecisionId, booking.TadssDecisionId);
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, invoice.PaymentStatus);
        Assert.Null(invoice.DepositSource);
        Assert.Empty(await ReloadLocksAsync(order.Booking.Id));
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.False(c.IsActive));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-06")]
    public async Task Reject_FreesTheSlotForAnotherBooking()
    {
        var order = await SeedPendingApprovalAsync();
        Assert.True(await Repository.RejectByManagerAsync(order.Booking.Id, null, Note, Now));

        var customer = await Seeder.SeedCustomerAsync();
        var other = TestDataSeeder.NewBooking(order.Seed.Branch.Id, customer.Id, BookingStatus.PENDING, Now);
        DbContext.Bookings.Add(other);
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(other.Id, order.Seed.Court.Id, PlayDate, Start, End));
        DbContext.SlotLocks.Add(TestDataSeeder.NewSlotLock(other.Id, order.Seed.Court.Id, PlayDate, Start, End, Now.AddMinutes(10)));

        await DbContext.SaveChangesAsync(); // exclusion constraint không chặn: sân đã nhả
    }

    [Fact]
    public async Task Reject_WithoutKnownResolver_LeavesResolverAndCancellerEmpty()
    {
        var order = await SeedPendingApprovalAsync();

        Assert.True(await Repository.RejectByManagerAsync(order.Booking.Id, null, Note, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Null(booking.ApprovalResolvedBy);
        Assert.Null(booking.CancelledBy);
        Assert.Equal(ApprovalResult.REJECTED, booking.ApprovalResult);
    }

    // ───────────────────────── Hàng chờ ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task PendingApprovals_ListsOnlyWaitingInTimeOfTheBranch_SoonestFirstWithDetails()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var tier = await Seeder.SeedTierAsync(100);
        var later = await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: Now.AddMinutes(25), playDate: PlayDate);
        var sooner = await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: Now.AddMinutes(5), playDate: PlayDate.AddDays(1));
        await Seeder.SeedCustomerLoyaltyAsync(await DbContext.Users.SingleAsync(u => u.Id == sooner.Booking.CustomerId), tier);
        await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: Now, playDate: PlayDate.AddDays(2));                // hết hạn đúng lúc này
        await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: Now.AddMinutes(-3), playDate: PlayDate.AddDays(3)); // đã quá hạn
        // Đã rời hàng chờ nhưng còn giữ hạn duyệt trong tương lai: đã duyệt (PENDING / CONFIRMED), khách hủy khi đang chờ (CANCELLED)
        await SeedPendingApprovalAsync(seed: seed, status: BookingStatus.PENDING, approvalExpiresAt: Now.AddMinutes(2), playDate: PlayDate.AddDays(4));
        await SeedPendingApprovalAsync(seed: seed, status: BookingStatus.CONFIRMED, approvalExpiresAt: Now.AddMinutes(2), playDate: PlayDate.AddDays(6));
        await SeedPendingApprovalAsync(seed: seed, status: BookingStatus.CANCELLED, approvalExpiresAt: Now.AddMinutes(2), playDate: PlayDate.AddDays(5));
        await SeedPendingApprovalAsync(approvalExpiresAt: Now.AddMinutes(1)); // chi nhánh khác
        DbContext.ChangeTracker.Clear();

        var page = await Repository.GetPendingApprovalsAsync(seed.Branch.Id, Now, 1, 10);

        Assert.Equal([sooner.Booking.Id, later.Booking.Id], page.Items.Select(b => b.Id));
        Assert.Equal(2, page.TotalItems);
        var first = page.Items.First();
        Assert.Equal(seed.Branch.Name, first.Branch.Name);
        Assert.NotNull(first.Customer);
        Assert.Equal(tier.Name, first.Customer!.CustomerLoyalty!.Tier.Name);
        Assert.Equal(seed.Court.Name, Assert.Single(first.BookingCourts).Court.Name);
        Assert.Equal(200_000m, first.Invoice!.FinalTotal);
        Assert.Equal(["Đơn từ 2.000.000 đ cần duyệt."], first.ApprovalReasons);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task PendingApprovals_TiesOnExpiryAreOrderedByCreatedAtThenId()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var expires = Now.AddMinutes(10);
        var newer = await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: expires, playDate: PlayDate);
        var older = await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: expires, playDate: PlayDate.AddDays(1));
        older.Booking.CreatedAt = CreatedAt.AddMinutes(-5);
        var sameA = await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: expires, playDate: PlayDate.AddDays(2));
        var sameB = await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: expires, playDate: PlayDate.AddDays(3));
        sameA.Booking.CreatedAt = sameB.Booking.CreatedAt = CreatedAt.AddMinutes(1);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var ids = (await Repository.GetPendingApprovalsAsync(seed.Branch.Id, Now, 1, 10)).Items.Select(b => b.Id).ToList();

        var tied = new[] { sameA.Booking.Id, sameB.Booking.Id }.OrderBy(id => id).ToList();
        Assert.Equal(new[] { older.Booking.Id, newer.Booking.Id, tied[0], tied[1] }, ids);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task PendingApprovals_PagesWithoutGapsOrRepeats()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var created = new List<Guid>();
        for (var i = 0; i < 5; i++)
            created.Add((await SeedPendingApprovalAsync(seed: seed, approvalExpiresAt: Now.AddMinutes(10 + i), playDate: PlayDate.AddDays(i))).Booking.Id);
        DbContext.ChangeTracker.Clear();

        var first = await Repository.GetPendingApprovalsAsync(seed.Branch.Id, Now, 1, 2);
        var second = await Repository.GetPendingApprovalsAsync(seed.Branch.Id, Now, 2, 2);
        var third = await Repository.GetPendingApprovalsAsync(seed.Branch.Id, Now, 3, 2);

        Assert.Equal(created, first.Items.Concat(second.Items).Concat(third.Items).Select(b => b.Id));
        Assert.Equal(5, first.TotalItems);
        Assert.Equal(3, first.TotalPages);
        Assert.True(first.HasNext);
        Assert.False(third.HasNext);
        Assert.Single(third.Items);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task PendingApprovals_WithoutBranch_CoversEveryBranch()
    {
        var a = await SeedPendingApprovalAsync(approvalExpiresAt: Now.AddMinutes(3));
        var b = await SeedPendingApprovalAsync(approvalExpiresAt: Now.AddMinutes(4));
        DbContext.ChangeTracker.Clear();

        var ids = (await Repository.GetPendingApprovalsAsync(null, Now, 1, 50)).Items.Select(x => x.Id).ToList();

        Assert.Contains(a.Booking.Id, ids);
        Assert.Contains(b.Booking.Id, ids);
        Assert.True(ids.IndexOf(a.Booking.Id) < ids.IndexOf(b.Booking.Id));
    }

    [Fact]
    public async Task PendingApprovals_NowNotUtc_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repository.GetPendingApprovalsAsync(null, DateTime.SpecifyKind(Now, DateTimeKind.Local), 1, 10));
    }
}
