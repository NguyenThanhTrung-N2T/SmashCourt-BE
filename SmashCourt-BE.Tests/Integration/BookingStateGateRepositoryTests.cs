using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Câu ghi có điều kiện của check-in, thêm/bớt dịch vụ, thanh toán lại và trạng thái hóa đơn, trên schema thật.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BookingStateGateRepositoryTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    private InvoiceRepository Invoices => new(DbContext);

    // ───────────────────────── Check-in ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    public async Task CheckIn_FromTheReadStatus_StartsPlayAndClosesTheCancelLink(BookingStatus from)
    {
        var order = await SeedOrderAsync(from);
        order.Booking.CancelTokenHash = CancelTokenHelper.Hash("raw");
        order.Booking.CancelTokenExpiresAt = Now.AddHours(2);
        await DbContext.SaveChangesAsync();

        Assert.True(await Repository.CheckInAsync(order.Booking.Id, from, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
        Assert.Equal(Now, booking.CheckedInAt);
        Assert.Equal(Now, booking.CancelTokenUsedAt);
        Assert.Equal(Now, booking.UpdatedAt);
    }

    [Fact]
    public async Task CheckIn_WithoutCancelLink_LeavesTokenColumnsEmpty()
    {
        var order = await SeedOrderAsync(BookingStatus.CONFIRMED);

        Assert.True(await Repository.CheckInAsync(order.Booking.Id, BookingStatus.CONFIRMED, Now));

        Assert.Null((await ReloadBookingAsync(order.Booking.Id)).CancelTokenUsedAt);
    }

    [Fact]
    public async Task CheckIn_LinkAlreadyUsed_KeepsTheFirstUseTime()
    {
        var order = await SeedOrderAsync(BookingStatus.CONFIRMED);
        order.Booking.CancelTokenHash = CancelTokenHelper.Hash("raw");
        order.Booking.CancelTokenExpiresAt = Now.AddHours(2);
        order.Booking.CancelTokenUsedAt = CreatedAt;
        await DbContext.SaveChangesAsync();

        Assert.True(await Repository.CheckInAsync(order.Booking.Id, BookingStatus.CONFIRMED, Now));

        Assert.Equal(CreatedAt, (await ReloadBookingAsync(order.Booking.Id)).CancelTokenUsedAt);
    }

    [Fact]
    public async Task CheckIn_StatusChangedSinceRead_Loses()
    {
        var order = await SeedOrderAsync(BookingStatus.IN_PROGRESS);

        Assert.False(await Repository.CheckInAsync(order.Booking.Id, BookingStatus.CONFIRMED, Now));

        Assert.Null((await ReloadBookingAsync(order.Booking.Id)).CheckedInAt);
    }

    // ───────────────────────── Thêm / bớt dịch vụ ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.IN_PROGRESS, true)]
    [InlineData(BookingStatus.PENDING_PAYMENT, true)]
    [InlineData(BookingStatus.COMPLETED, false)]
    [InlineData(BookingStatus.CONFIRMED, false)]
    [InlineData(BookingStatus.CANCELLED, false)]
    public async Task LockForServiceChange_OnlyWhilePlayingOrWaitingForPayment(BookingStatus status, bool expected)
    {
        var order = await SeedOrderAsync(status);

        Assert.Equal(expected, await Repository.TryLockForServiceChangeAsync(order.Booking.Id, Now));
    }

    [Fact]
    public async Task LockForServiceChange_OutsideTransaction_Throws()
    {
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.TryLockForServiceChangeAsync(Guid.NewGuid(), Now));
    }

    // ───────────────────────── Thanh toán lại ─────────────────────────

    [Fact]
    public async Task ExtendPaymentWindow_PendingOrder_MovesInvoiceAndLocksTogether()
    {
        var order = await SeedOrderAsync(courtCount: 2);
        var newExpiry = CreatedAt.AddMinutes(13);

        Assert.True(await Repository.ExtendPaymentWindowAsync(order.Booking.Id, newExpiry, Now));

        Assert.Equal(newExpiry, (await ReloadInvoiceAsync(order.Booking.Id)).ExpiresAt);
        Assert.All(await ReloadLocksAsync(order.Booking.Id), l => Assert.Equal(newExpiry, l.ExpiresAt));
        Assert.Equal(CreatedAt.AddMinutes(15), (await ReloadInvoiceAsync(order.Booking.Id)).HoldDeadline);
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    public async Task ExtendPaymentWindow_OrderNoLongerPending_Loses(BookingStatus status)
    {
        var order = await SeedOrderAsync(status);

        Assert.False(await Repository.ExtendPaymentWindowAsync(order.Booking.Id, CreatedAt.AddMinutes(13), Now));

        Assert.Equal(CreatedAt.AddMinutes(10), (await ReloadInvoiceAsync(order.Booking.Id)).ExpiresAt);
    }

    // ───────────────────────── Trạng thái hóa đơn ─────────────────────────

    [Fact]
    public async Task SetPaymentStatus_ChangesOnlyTheStatus()
    {
        var order = await SeedOrderAsync();
        var before = await ReloadInvoiceAsync(order.Booking.Id);

        await Invoices.SetPaymentStatusAsync(order.Invoice.Id, InvoicePaymentStatus.PAID, Now);

        var after = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(InvoicePaymentStatus.PAID, after.PaymentStatus);
        Assert.Equal(Now, after.UpdatedAt);
        Assert.Equal(before.FinalTotal, after.FinalTotal);
        Assert.Equal(before.ServiceFee, after.ServiceFee);
        Assert.Equal(before.ExpiresAt, after.ExpiresAt);
    }
}
