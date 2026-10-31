using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Lõi hủy dùng chung (D1, P6-14): một lệnh UPDATE có điều kiện, ghi đủ cột hủy; chạy trên schema thật nên
/// chk_bookings_cancelled_at và chk_bookings_cancel_reason là lưới an toàn cuối.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-17")]
public sealed class BookingCancelWithStatusCheckTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    private static readonly BookingStatus[] CancellableSources =
        [BookingStatus.PENDING, BookingStatus.PENDING_APPROVAL, BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE];

    [Fact]
    public async Task CancelWithStatusCheckAsync_WhenStatusMatches_WritesEveryCancelColumnInOneStatement()
    {
        var order = await SeedOrderAsync();
        var staff = await Seeder.SeedCustomerAsync(); // cancelled_by chỉ cần là user hợp lệ

        var rows = await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.STAFF, BookingCancelReason.APPROVAL_REJECTED, staff.Id, Now);

        Assert.Equal(1, rows);
        var persisted = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.CANCELLED, persisted.Status);
        Assert.Equal(Now, persisted.CancelledAt);
        Assert.Equal(DateTimeKind.Utc, persisted.CancelledAt!.Value.Kind);
        Assert.Equal(CancelSourceEnum.STAFF, persisted.CancelSource);
        Assert.Equal(BookingCancelReason.APPROVAL_REJECTED, persisted.CancelReason);
        Assert.Equal(staff.Id, persisted.CancelledBy);
        Assert.Equal(Now, persisted.UpdatedAt);
    }

    [Fact]
    public async Task CancelWithStatusCheckAsync_WhenCancelledByIsNull_LeavesCancelledByNull()
    {
        var order = await SeedOrderAsync();

        await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now);

        var persisted = await ReloadBookingAsync(order.Booking.Id);
        Assert.Null(persisted.CancelledBy);
        Assert.Equal(BookingCancelReason.POLICY_REJECTED, persisted.CancelReason);
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    public async Task CancelWithStatusCheckAsync_WhenCurrentStatusIsAnyOfSeveralExpected_Cancels(BookingStatus current)
    {
        var order = await SeedOrderAsync(current);

        var rows = await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, CancellableSources, BookingStatus.CANCELLED,
            CancelSourceEnum.CUSTOMER, BookingCancelReason.CUSTOMER_CANCELLED, null, Now);

        Assert.Equal(1, rows);
        Assert.Equal(BookingStatus.CANCELLED, (await ReloadBookingAsync(order.Booking.Id)).Status);
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    [InlineData(BookingStatus.CANCELLED)]
    public async Task CancelWithStatusCheckAsync_WhenNewStatusIsAnyCancelledStatus_SatisfiesDbConstraints(BookingStatus newStatus)
    {
        var order = await SeedOrderAsync(BookingStatus.PAID_ONLINE);

        var rows = await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PAID_ONLINE], newStatus,
            CancelSourceEnum.CUSTOMER, BookingCancelReason.CUSTOMER_CANCELLED, null, Now);

        Assert.Equal(1, rows);
        var persisted = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(newStatus, persisted.Status);
        Assert.NotNull(persisted.CancelledAt);
    }

    [Theory]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    [InlineData(BookingStatus.IN_PROGRESS)]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    public async Task CancelWithStatusCheckAsync_WhenCurrentStatusNotExpected_ReturnsZeroAndChangesNothing(BookingStatus current)
    {
        var order = await SeedOrderAsync(current);
        var before = await ReloadBookingAsync(order.Booking.Id);

        var rows = await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now);

        Assert.Equal(0, rows);
        var after = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Null(after.CancelledAt);
        Assert.Null(after.CancelSource);
        Assert.Null(after.CancelReason);
    }

    [Fact]
    public async Task CancelWithStatusCheckAsync_WhenAlreadyCancelled_DoesNotOverwriteFirstCancellation()
    {
        var order = await SeedOrderAsync();
        await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.STAFF, BookingCancelReason.STAFF_CANCELLED, null, Now);

        var second = await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, CancellableSources, BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.PAYMENT_EXPIRED, null, Now.AddMinutes(10));

        Assert.Equal(0, second);
        var persisted = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(CancelSourceEnum.STAFF, persisted.CancelSource);
        Assert.Equal(BookingCancelReason.STAFF_CANCELLED, persisted.CancelReason);
        Assert.Equal(Now, persisted.CancelledAt);
    }

    [Fact]
    public async Task CancelWithStatusCheckAsync_WhenBookingDoesNotExist_ReturnsZero()
    {
        var rows = await Repository.CancelWithStatusCheckAsync(
            Guid.NewGuid(), [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now);

        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task CancelWithStatusCheckAsync_OnlyAffectsTheGivenBooking()
    {
        var first = await SeedOrderAsync();
        var second = await SeedOrderAsync(seed: first.Seed, playDate: PlayDate.AddDays(1)); // cùng sân, ngày khác để không vi phạm ràng buộc chồng giờ

        await Repository.CancelWithStatusCheckAsync(
            first.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now);

        Assert.Equal(BookingStatus.PENDING, (await ReloadBookingAsync(second.Booking.Id)).Status);
    }

    [Fact]
    public async Task CancelWithStatusCheckAsync_DoesNotTouchInvoiceCourtsOrSlotLocks()
    {
        var order = await SeedOrderAsync(courtCount: 2);

        await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now);

        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(InvoicePaymentStatus.UNPAID, invoice.PaymentStatus);
        Assert.Equal(order.Invoice.ExpiresAt, invoice.ExpiresAt);
        Assert.All(await ReloadCourtsAsync(order.Booking.Id), c => Assert.True(c.IsActive));
        Assert.Equal(2, (await ReloadLocksAsync(order.Booking.Id)).Count);
    }

    [Fact]
    public async Task CancelWithStatusCheckAsync_BypassesChangeTracker_SoTrackedEntityIsStaleUntilReloaded()
    {
        var order = await SeedOrderAsync();

        await Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now);

        // Hợp đồng cho caller: entity đang track vẫn là PENDING, phải đọc lại (hoặc ClearChangeTracker) trước khi dựng response
        Assert.Equal(BookingStatus.PENDING, order.Booking.Status);
        Assert.Equal(BookingStatus.CANCELLED, (await ReloadBookingAsync(order.Booking.Id)).Status);
    }

    [Fact]
    public async Task CancelWithStatusCheckAsync_WhenNoExpectedStatus_Throws()
    {
        var order = await SeedOrderAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [], BookingStatus.CANCELLED, CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now));
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.COMPLETED)]
    public async Task CancelWithStatusCheckAsync_WhenNewStatusIsNotCancelled_Throws(BookingStatus newStatus)
    {
        var order = await SeedOrderAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], newStatus, CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, Now));
        Assert.Equal(BookingStatus.PENDING, (await ReloadBookingAsync(order.Booking.Id)).Status);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task CancelWithStatusCheckAsync_WhenTimeIsNotUtc_ThrowsAndChangesNothing(DateTimeKind kind)
    {
        var order = await SeedOrderAsync();
        var notUtc = DateTime.SpecifyKind(Now, kind);

        await Assert.ThrowsAsync<ArgumentException>(() => Repository.CancelWithStatusCheckAsync(
            order.Booking.Id, [BookingStatus.PENDING], BookingStatus.CANCELLED,
            CancelSourceEnum.SYSTEM, BookingCancelReason.POLICY_REJECTED, null, notUtc));
        Assert.Equal(BookingStatus.PENDING, (await ReloadBookingAsync(order.Booking.Id)).Status);
    }
}
