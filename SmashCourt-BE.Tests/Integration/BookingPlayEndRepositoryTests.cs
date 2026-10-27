using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Câu đọc / ghi của Job-04 (NO_SHOW) và Job-02 (kết thúc giờ chơi) trên schema thật (gói 6A-5 Task 5, B6, B7 — P6-25): chọn ứng viên,
/// câu cập nhật có điều kiện (thua thì không đổi gì), "sân còn bận" theo luật cũ, trả sân về AVAILABLE trừ sân SUSPENDED/INACTIVE.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BookingPlayEndRepositoryTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    private CourtRepository Courts => new(DbContext);
    private InvoiceRepository Invoices => new(DbContext);

    private async Task<PendingOrder> SeedCheckedInAsync(BookingStatus status, DateOnly? playDate = null)
    {
        var order = await SeedOrderAsync(status, playDate: playDate);
        order.Booking.CheckedInAt = CreatedAt.AddMinutes(1);
        await DbContext.SaveChangesAsync();
        return order;
    }

    // ───────────────────────── MarkNoShowAsync ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    public async Task MarkNoShow_NotCheckedInAndStatusUnchanged_Wins(BookingStatus status)
    {
        var order = await SeedOrderAsync(status);

        Assert.True(await Repository.MarkNoShowAsync(order.Booking.Id, status, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.NO_SHOW, booking.Status);
        Assert.Equal(Now, booking.UpdatedAt);
        Assert.Null(booking.CheckedInAt);
    }

    [Fact]
    public async Task MarkNoShow_StaffJustCheckedIn_LosesAndChangesNothing()
    {
        // B6: lần check-in sát mốc giờ chơi + 15' đã commit (IN_PROGRESS kèm checked_in_at)
        var order = await SeedCheckedInAsync(BookingStatus.IN_PROGRESS);

        Assert.False(await Repository.MarkNoShowAsync(order.Booking.Id, BookingStatus.CONFIRMED, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
        Assert.Equal(CreatedAt, booking.UpdatedAt);
    }

    [Fact]
    public async Task MarkNoShow_CheckedInAtSetEvenIfStatusUnchanged_Loses()
    {
        // Dữ liệu cũ: CONFIRMED mà đã có checked_in_at ⇒ khách đã tới, không được đánh NO_SHOW
        var order = await SeedCheckedInAsync(BookingStatus.CONFIRMED);

        Assert.False(await Repository.MarkNoShowAsync(order.Booking.Id, BookingStatus.CONFIRMED, Now));

        Assert.Equal(BookingStatus.CONFIRMED, (await ReloadBookingAsync(order.Booking.Id)).Status);
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)] // nhân viên vừa hủy đơn đã trả: không được đè mất tiền hoàn
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.PAID_ONLINE)]              // trạng thái khác trạng thái đã đọc
    public async Task MarkNoShow_StatusChangedSinceRead_LosesAndChangesNothing(BookingStatus current)
    {
        var order = await SeedOrderAsync(current);

        Assert.False(await Repository.MarkNoShowAsync(order.Booking.Id, BookingStatus.CONFIRMED, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(current, booking.Status);
        Assert.Equal(CreatedAt, booking.UpdatedAt);
    }

    [Theory]
    [InlineData(BookingStatus.IN_PROGRESS)]
    [InlineData(BookingStatus.PENDING)]
    public async Task MarkNoShow_FromAStatusThatCannotBeNoShow_Throws(BookingStatus expected)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repository.MarkNoShowAsync(Guid.NewGuid(), expected, Now));
    }

    // ───────────────────────── EndPlayAsync ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.PENDING_PAYMENT)]
    public async Task EndPlay_StatusUnchanged_WritesTargetStatusAndOnlyTouchesInvoiceUpdatedAt(BookingStatus target)
    {
        var order = await SeedCheckedInAsync(BookingStatus.IN_PROGRESS);
        var invoiceBefore = await ReloadInvoiceAsync(order.Booking.Id);

        Assert.True(await Repository.EndPlayAsync(order.Booking.Id, BookingStatus.IN_PROGRESS, target, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(target, booking.Status);
        Assert.Equal(Now, booking.UpdatedAt);
        var invoice = await ReloadInvoiceAsync(order.Booking.Id);
        Assert.Equal(Now, invoice.UpdatedAt);
        Assert.Equal(invoiceBefore.PaymentStatus, invoice.PaymentStatus);
        Assert.Equal(invoiceBefore.FinalTotal, invoice.FinalTotal);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]       // nhân viên vừa check-out
    [InlineData(BookingStatus.PENDING_PAYMENT)]
    public async Task EndPlay_StatusChangedSinceRead_LosesAndChangesNothing(BookingStatus current)
    {
        var order = await SeedCheckedInAsync(current);

        Assert.False(await Repository.EndPlayAsync(order.Booking.Id, BookingStatus.IN_PROGRESS, BookingStatus.PENDING_PAYMENT, Now));

        Assert.Equal(current, (await ReloadBookingAsync(order.Booking.Id)).Status);
        Assert.NotEqual(Now, (await ReloadInvoiceAsync(order.Booking.Id)).UpdatedAt);
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.NO_SHOW)]
    [InlineData(BookingStatus.IN_PROGRESS)]
    public async Task EndPlay_TargetIsNotAnEndOfPlayStatus_Throws(BookingStatus target)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Repository.EndPlayAsync(Guid.NewGuid(), BookingStatus.IN_PROGRESS, target, Now));
    }

    [Fact]
    public async Task ConditionalWrites_OutsideTransaction_Throw()
    {
        using var scope = Fixture.CreateScope();
        var repository = new BookingRepository(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.MarkNoShowAsync(Guid.NewGuid(), BookingStatus.CONFIRMED, Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.EndPlayAsync(Guid.NewGuid(), BookingStatus.IN_PROGRESS, BookingStatus.COMPLETED, Now));
    }

    // ───────────────────────── Ứng viên ─────────────────────────

    [Fact]
    public async Task GetNoShowCandidates_OnlyNotCheckedInConfirmedOrPaidOnline_UpToToday_WithCourtsLoaded()
    {
        var confirmed = await SeedOrderAsync(BookingStatus.CONFIRMED);
        var paidOnline = await SeedOrderAsync(BookingStatus.PAID_ONLINE, playDate: PlayDate.AddDays(-1));
        var future = await SeedOrderAsync(BookingStatus.CONFIRMED, playDate: PlayDate.AddDays(1));
        var checkedIn = await SeedCheckedInAsync(BookingStatus.CONFIRMED);
        var playing = await SeedCheckedInAsync(BookingStatus.IN_PROGRESS);
        var pending = await SeedOrderAsync(BookingStatus.PENDING);
        var mine = new[] { confirmed, paidOnline, future, checkedIn, playing, pending }.Select(o => o.Booking.Id).ToHashSet();

        var candidates = (await Repository.GetNoShowCandidatesAsync(PlayDate)).Where(b => mine.Contains(b.Id)).ToList();

        Assert.Equal(new[] { confirmed.Booking.Id, paidOnline.Booking.Id }.OrderBy(i => i), candidates.Select(b => b.Id).OrderBy(i => i));
        Assert.All(candidates, b => Assert.All(b.BookingCourts, bc => Assert.NotNull(bc.Court)));
        Assert.All(candidates, b => Assert.NotNull(b.Invoice));
    }

    [Fact]
    public async Task GetPlayEndCandidates_InProgressOrCheckedInConfirmed_UpToToday()
    {
        var playing = await SeedCheckedInAsync(BookingStatus.IN_PROGRESS);
        var legacyCheckedIn = await SeedCheckedInAsync(BookingStatus.PAID_ONLINE, playDate: PlayDate.AddDays(-1));
        var notCheckedIn = await SeedOrderAsync(BookingStatus.CONFIRMED);
        var future = await SeedCheckedInAsync(BookingStatus.IN_PROGRESS, playDate: PlayDate.AddDays(1));
        var completed = await SeedCheckedInAsync(BookingStatus.COMPLETED);
        var mine = new[] { playing, legacyCheckedIn, notCheckedIn, future, completed }.Select(o => o.Booking.Id).ToHashSet();

        var candidates = (await Repository.GetPlayEndCandidatesAsync(PlayDate)).Where(b => mine.Contains(b.Id)).ToList();

        Assert.Equal(new[] { playing.Booking.Id, legacyCheckedIn.Booking.Id }.OrderBy(i => i), candidates.Select(b => b.Id).OrderBy(i => i));
        Assert.All(candidates, b => Assert.All(b.BookingCourts, bc => Assert.NotNull(bc.Court)));
    }

    // ───────────────────────── Sân còn bận ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.CONFIRMED, true)]
    [InlineData(BookingStatus.PAID_ONLINE, true)]
    [InlineData(BookingStatus.IN_PROGRESS, true)]
    [InlineData(BookingStatus.PENDING, false)]     // luật cũ của job: chỉ ba trạng thái đang hiệu lực
    [InlineData(BookingStatus.COMPLETED, false)]
    public async Task GetCourtsHeldByOtherBookings_CountsOnlyOtherActiveOrders(BookingStatus otherStatus, bool held)
    {
        var finished = await SeedOrderAsync(BookingStatus.IN_PROGRESS);
        await SeedOrderAsync(otherStatus, seed: finished.Seed, playDate: PlayDate.AddDays(2));
        var courtId = finished.Courts.Single().Id;

        var result = await Repository.GetCourtsHeldByOtherBookingsAsync([courtId], finished.Booking.Id);

        Assert.Equal(held, result.Contains(courtId));
    }

    [Fact]
    public async Task GetCourtsHeldByOtherBookings_IgnoresTheOrderItselfAndInactiveCourtRows()
    {
        var finished = await SeedOrderAsync(BookingStatus.IN_PROGRESS);
        var other = await SeedOrderAsync(BookingStatus.CONFIRMED, seed: finished.Seed, playDate: PlayDate.AddDays(2));
        await Repository.UpdateCourtActiveStatusAsync(other.Booking.Id, false);
        var courtId = finished.Courts.Single().Id;

        Assert.Empty(await Repository.GetCourtsHeldByOtherBookingsAsync([courtId], finished.Booking.Id));
    }

    // ───────────────────────── Trả sân, đọc hóa đơn ─────────────────────────

    [Theory]
    [InlineData(CourtStatus.IN_USE, CourtStatus.AVAILABLE)]
    [InlineData(CourtStatus.LOCKED, CourtStatus.AVAILABLE)]
    [InlineData(CourtStatus.AVAILABLE, CourtStatus.AVAILABLE)]
    [InlineData(CourtStatus.SUSPENDED, CourtStatus.SUSPENDED)]
    [InlineData(CourtStatus.INACTIVE, CourtStatus.INACTIVE)]
    public async Task ReleaseToAvailable_SkipsSuspendedAndInactiveCourts(CourtStatus before, CourtStatus after)
    {
        var order = await SeedOrderAsync();
        var court = order.Courts.Single();
        court.Status = before;
        await DbContext.SaveChangesAsync();

        await Courts.ReleaseToAvailableAsync([court.Id], Now);

        Assert.Equal(after, (await DbContext.Courts.AsNoTracking().SingleAsync(c => c.Id == court.Id)).Status);
    }

    [Fact]
    public async Task GetPaymentStatusByBookingId_ReadsTheCurrentValue()
    {
        var order = await SeedOrderAsync();
        await Invoices.SetPaymentStatusAsync(order.Invoice.Id, InvoicePaymentStatus.PAID, Now);

        Assert.Equal(InvoicePaymentStatus.PAID, await Invoices.GetPaymentStatusByBookingIdAsync(order.Booking.Id));
        Assert.Null(await Invoices.GetPaymentStatusByBookingIdAsync(Guid.NewGuid()));
    }
}
