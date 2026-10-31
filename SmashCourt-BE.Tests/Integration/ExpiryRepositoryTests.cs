using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Truy vấn của job hết hạn thanh toán (Job-01) và job hết hạn duyệt trên schema thật (gói 6A-5 Task 1, 3, 4): chọn đơn quá hạn
/// (<c>&lt;= now</c>, hạn sớm nhất trước, giới hạn số đơn) và kiểm lại hạn dưới khóa. Biên tính tới µs (độ chính xác của timestamptz).
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExpiryRepositoryTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    // ───────────────────────── Job hết hạn: kiểm lại hạn dưới khóa (6A-5) ─────────────────────────

    // C4: "hết hạn" là expires_at <= now, cùng định nghĩa với Thanh toán lại; biên tính tới µs (độ chính xác của timestamptz)
    [Theory]
    [InlineData(-10, true)]
    [InlineData(0, true)]
    [InlineData(10, false)]
    public async Task IsPaymentWindowOver_AtTheBoundary_UsesLessOrEqual(long offsetTicks, bool expected)
    {
        var order = await SeedOrderAsync();
        order.Invoice.ExpiresAt = Now.AddTicks(offsetTicks);
        await DbContext.SaveChangesAsync();

        Assert.Equal(expected, await Repository.IsPaymentWindowOverAsync(order.Booking.Id, Now));
    }

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    public async Task IsPaymentWindowOver_OrderNoLongerPending_IsFalse(BookingStatus status)
    {
        var order = await SeedOrderAsync(status);
        order.Invoice.ExpiresAt = Now.AddTicks(-10);
        await DbContext.SaveChangesAsync();

        Assert.False(await Repository.IsPaymentWindowOverAsync(order.Booking.Id, Now));
    }

    [Theory]
    [InlineData(InvoicePaymentStatus.PARTIALLY_PAID)]
    [InlineData(InvoicePaymentStatus.PAID)]
    [InlineData(InvoicePaymentStatus.EXPIRED)]
    public async Task IsPaymentWindowOver_InvoiceNotUnpaid_IsFalse(InvoicePaymentStatus invoiceStatus)
    {
        var order = await SeedOrderAsync();
        order.Invoice.ExpiresAt = Now.AddTicks(-10);
        order.Invoice.PaymentStatus = invoiceStatus;
        await DbContext.SaveChangesAsync();

        Assert.False(await Repository.IsPaymentWindowOverAsync(order.Booking.Id, Now));
    }

    [Fact]
    public async Task IsPaymentWindowOver_InvoiceWithoutDeadline_IsFalse()
    {
        var order = await SeedOrderAsync();
        order.Invoice.ExpiresAt = null;
        await DbContext.SaveChangesAsync();

        Assert.False(await Repository.IsPaymentWindowOverAsync(order.Booking.Id, Now));
    }

    [Fact]
    public async Task IsPaymentWindowOver_RightAfterTheWindowIsExtended_IsFalse()
    {
        // B2: Thanh toán lại vừa gia hạn (cùng transaction ở đây; ở job là lần gia hạn đã commit trước khi job khóa được đơn)
        var order = await SeedOrderAsync();
        order.Invoice.ExpiresAt = Now.AddSeconds(-1);
        await DbContext.SaveChangesAsync();
        Assert.True(await Repository.IsPaymentWindowOverAsync(order.Booking.Id, Now));

        Assert.True(await Repository.ExtendPaymentWindowAsync(order.Booking.Id, Now.AddMinutes(10), Now));

        Assert.False(await Repository.IsPaymentWindowOverAsync(order.Booking.Id, Now));
    }

    [Fact]
    public async Task IsPaymentWindowOver_UnknownBooking_IsFalse()
    {
        Assert.False(await Repository.IsPaymentWindowOverAsync(Guid.NewGuid(), Now));
    }

    // Phần bù của điều kiện duyệt / từ chối (approval_expires_at > now)
    [Theory]
    [InlineData(-10, true)]
    [InlineData(0, true)]
    [InlineData(10, false)]
    public async Task IsApprovalWindowOver_AtTheBoundary_UsesLessOrEqual(long offsetTicks, bool expected)
    {
        var order = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL);
        order.Booking.ApprovalExpiresAt = Now.AddTicks(offsetTicks);
        await DbContext.SaveChangesAsync();

        Assert.Equal(expected, await Repository.IsApprovalWindowOverAsync(order.Booking.Id, Now));
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.CANCELLED)]
    public async Task IsApprovalWindowOver_OrderAlreadyResolved_IsFalse(BookingStatus status)
    {
        var order = await SeedOrderAsync(status);

        Assert.False(await Repository.IsApprovalWindowOverAsync(order.Booking.Id, Now));
    }

    // ───────────────────────── Job-01: chọn đơn hết hạn thanh toán (6A-5 Task 3) ─────────────────────────

    // Kết quả lọc theo đơn của test (DB dùng chung với test khác trong collection)
    private async Task<List<Guid>> ExpiredUnpaidAmongAsync(IEnumerable<Guid> seeded, int take = 100)
    {
        var mine = seeded.ToHashSet();
        return (await Repository.GetExpiredUnpaidIdsAsync(Now, take)).Where(mine.Contains).ToList();
    }

    private async Task<PendingOrder> SeedExpiringAtAsync(DateTime expiresAt, BookingStatus status = BookingStatus.PENDING,
        InvoicePaymentStatus invoiceStatus = InvoicePaymentStatus.UNPAID)
    {
        var order = await SeedOrderAsync(status);
        order.Invoice.ExpiresAt = expiresAt;
        order.Invoice.PaymentStatus = invoiceStatus;
        await DbContext.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task GetExpiredUnpaidIds_TakesOrdersExpiringAtOrBeforeNow_EarliestFirst()
    {
        var atNow = await SeedExpiringAtAsync(Now);
        var later = await SeedExpiringAtAsync(Now.AddTicks(10));
        var justBefore = await SeedExpiringAtAsync(Now.AddTicks(-10));
        var earliest = await SeedExpiringAtAsync(CreatedAt.AddSeconds(1));

        var ids = await ExpiredUnpaidAmongAsync([atNow.Booking.Id, later.Booking.Id, justBefore.Booking.Id, earliest.Booking.Id]);

        Assert.Equal([earliest.Booking.Id, justBefore.Booking.Id, atNow.Booking.Id], ids);
    }

    [Theory]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.CANCELLED)]
    public async Task GetExpiredUnpaidIds_SkipsOrdersNoLongerPending(BookingStatus status)
    {
        var order = await SeedExpiringAtAsync(Now.AddTicks(-10), status);

        Assert.Empty(await ExpiredUnpaidAmongAsync([order.Booking.Id]));
    }

    [Theory]
    [InlineData(InvoicePaymentStatus.PARTIALLY_PAID)]
    [InlineData(InvoicePaymentStatus.PAID)]
    [InlineData(InvoicePaymentStatus.EXPIRED)]
    public async Task GetExpiredUnpaidIds_SkipsInvoicesNotUnpaid(InvoicePaymentStatus invoiceStatus)
    {
        var order = await SeedExpiringAtAsync(Now.AddTicks(-10), invoiceStatus: invoiceStatus);

        Assert.Empty(await ExpiredUnpaidAmongAsync([order.Booking.Id]));
    }

    [Fact]
    public async Task GetExpiredUnpaidIds_RespectsTake()
    {
        await SeedExpiringAtAsync(CreatedAt.AddSeconds(1));
        await SeedExpiringAtAsync(CreatedAt.AddSeconds(1.5));
        await SeedExpiringAtAsync(Now.AddTicks(-10));

        Assert.Equal(2, (await Repository.GetExpiredUnpaidIdsAsync(Now, 2)).Count);
    }

    [Fact]
    public async Task GetExpiredUnpaidIds_RejectsNonUtcClockAndNonPositiveTake()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.GetExpiredUnpaidIdsAsync(DateTime.SpecifyKind(Now, DateTimeKind.Local), 100));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repository.GetExpiredUnpaidIdsAsync(Now, 0));
    }

    // ───────────────────────── Job hết hạn duyệt: chọn đơn (6A-5 Task 4) ─────────────────────────

    private async Task<PendingOrder> SeedApprovalExpiringAtAsync(DateTime approvalExpiresAt)
    {
        var order = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL);
        order.Booking.ApprovalExpiresAt = approvalExpiresAt;
        await DbContext.SaveChangesAsync();
        return order;
    }

    private async Task<List<Guid>> ExpiredApprovalAmongAsync(IEnumerable<Guid> seeded)
    {
        var mine = seeded.ToHashSet();
        return (await Repository.GetExpiredApprovalIdsAsync(Now, 100)).Where(mine.Contains).ToList();
    }

    [Fact]
    public async Task GetExpiredApprovalIds_TakesOrdersExpiringAtOrBeforeNow_EarliestFirst()
    {
        var atNow = await SeedApprovalExpiringAtAsync(Now);
        var later = await SeedApprovalExpiringAtAsync(Now.AddTicks(10));
        var justBefore = await SeedApprovalExpiringAtAsync(Now.AddTicks(-10));
        var earliest = await SeedApprovalExpiringAtAsync(CreatedAt);

        var ids = await ExpiredApprovalAmongAsync([atNow.Booking.Id, later.Booking.Id, justBefore.Booking.Id, earliest.Booking.Id]);

        Assert.Equal([earliest.Booking.Id, justBefore.Booking.Id, atNow.Booking.Id], ids);
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]       // vừa được duyệt
    [InlineData(BookingStatus.CONFIRMED)]     // vừa được duyệt 0 %
    [InlineData(BookingStatus.CANCELLED)]     // vừa bị từ chối hoặc hủy
    public async Task GetExpiredApprovalIds_SkipsOrdersNoLongerWaitingForApproval(BookingStatus status)
    {
        // Đơn đã rời PENDING_APPROVAL vẫn giữ approval_expires_at cũ (đã qua)
        var order = await SeedOrderAsync(status);
        order.Booking.ApprovalExpiresAt = CreatedAt;
        await DbContext.SaveChangesAsync();

        Assert.Empty(await ExpiredApprovalAmongAsync([order.Booking.Id]));
    }

    [Fact]
    public async Task GetExpiredApprovalIds_RespectsTake()
    {
        await SeedApprovalExpiringAtAsync(CreatedAt);
        await SeedApprovalExpiringAtAsync(CreatedAt.AddSeconds(1));
        await SeedApprovalExpiringAtAsync(Now.AddTicks(-10));

        Assert.Equal(2, (await Repository.GetExpiredApprovalIdsAsync(Now, 2)).Count);
    }

    [Fact]
    public async Task GetExpiredApprovalIds_RejectsNonUtcClockAndNonPositiveTake()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Repository.GetExpiredApprovalIdsAsync(DateTime.SpecifyKind(Now, DateTimeKind.Local), 100));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repository.GetExpiredApprovalIdsAsync(Now, -1));
    }

    [Fact]
    public async Task WindowChecks_OutsideTransaction_Throw()
    {
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.IsPaymentWindowOverAsync(Guid.NewGuid(), Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.IsApprovalWindowOverAsync(Guid.NewGuid(), Now));
    }
}
