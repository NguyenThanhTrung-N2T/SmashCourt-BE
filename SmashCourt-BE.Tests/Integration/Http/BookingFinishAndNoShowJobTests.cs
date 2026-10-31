using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Job-02 (kết thúc giờ chơi) và Job-04 (NO_SHOW) trên host thật, DB thật (gói 6A-5 Task 5 bước 0). Viết trước khi sửa, xanh với mã cũ, để
/// bản viết lại có điều kiện (B6, B7 — P6-25) không làm lệch đường chạy bình thường: ân hạn 15', luật chọn COMPLETED / PENDING_PAYMENT theo hóa
/// đơn, tắt booking_courts, trả sân về AVAILABLE trừ sân SUSPENDED/INACTIVE hoặc còn đơn khác đang giữ, báo người chờ slot.
/// Hai job dùng đồng hồ thật nên giờ chơi được dời về hôm qua (đã qua), ngày mai (chưa tới) hoặc vừa bắt đầu 5 phút (trong ân hạn).
/// Hai job quét cả DB; mỗi test dùng sân riêng nên chỉ kiểm đơn và sân của mình.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class BookingFinishAndNoShowJobTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private static readonly TimeOnly PlayStart = new(10, 0);
    private static readonly TimeOnly PlayEnd = new(11, 0);

    private static DateOnly Yesterday => DateTimeHelper.GetTodayInVietnam().AddDays(-1);
    private static DateOnly Tomorrow => DateTimeHelper.GetTodayInVietnam().AddDays(1);

    // ───────────────────────── Dựng dữ liệu ─────────────────────────

    private async Task<Guid> ConfirmedWalkInAsync(BookableCourt court) =>
        await CreateWalkInAsync(await SeedStaffAsync(court.BranchId), court, payNow: false);

    private async Task<Guid> InProgressAsync(BookableCourt court, InvoicePaymentStatus invoiceStatus)
    {
        var order = await BookPaidAsync(court);
        await StartPlayingAsync(order.BookingId);
        await SetInvoiceStatusAsync(order.BookingId, invoiceStatus);
        return order.BookingId;
    }

    private Task MovePlayAsync(Guid bookingId, DateOnly date, TimeOnly? start = null, TimeOnly? end = null) =>
        WithDbAsync(async db =>
        {
            await db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.BookingDate, date));
            return await db.BookingCourts.Where(bc => bc.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(bc => bc.Date, date)
                .SetProperty(bc => bc.StartTime, start ?? PlayStart)
                .SetProperty(bc => bc.EndTime, end ?? PlayEnd));
        });

    private Task SetCourtStatusAsync(Guid courtId, CourtStatus status) =>
        WithDbAsync(db => db.Courts.Where(c => c.Id == courtId).ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status)));

    private Task SetInvoiceStatusAsync(Guid bookingId, InvoicePaymentStatus status) =>
        WithDbAsync(db => db.Invoices.Where(i => i.BookingId == bookingId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.PaymentStatus, status)
            .SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddHours(-1))));

    private Task<Court> ReadCourtAsync(Guid courtId) =>
        WithDbAsync(db => db.Courts.AsNoTracking().SingleAsync(c => c.Id == courtId));

    private Task<Invoice> ReadInvoiceAsync(Guid bookingId) =>
        WithDbAsync(db => db.Invoices.AsNoTracking().SingleAsync(i => i.BookingId == bookingId));

    private Task<List<BookingCourt>> ReadBookingCourtsAsync(Guid bookingId) =>
        WithDbAsync(db => db.BookingCourts.AsNoTracking().Where(bc => bc.BookingId == bookingId).ToListAsync());

    private async Task RunNoShowJobAsync()
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBookingJob>().DetectNoShowBookingsAsync();
    }

    private async Task RunFinishJobAsync()
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBookingJob>().ProcessExpiredActiveBookingsAsync();
    }

    // ───────────────────────── Job-04: NO_SHOW ─────────────────────────

    [Fact]
    public async Task NoShow_ConfirmedPastGrace_MarksNoShowAndReleasesTheCourt()
    {
        var court = await SeedCourtAsync();
        var bookingId = await ConfirmedWalkInAsync(court);
        await MovePlayAsync(bookingId, Yesterday);
        await SetCourtStatusAsync(court.CourtId, CourtStatus.IN_USE);

        await RunNoShowJobAsync();

        var booking = await RequireBookingAsync(bookingId);
        Assert.Equal(BookingStatus.NO_SHOW, booking.Status);
        Assert.Null(booking.CheckedInAt);
        Assert.All(await ReadBookingCourtsAsync(bookingId), bc => Assert.False(bc.IsActive));
        Assert.Equal(CourtStatus.AVAILABLE, (await ReadCourtAsync(court.CourtId)).Status);
    }

    [Fact]
    public async Task NoShow_PaidOnlinePastGrace_MarksNoShow()
    {
        var order = await BookPaidAsync();
        await MovePlayAsync(order.BookingId, Yesterday);

        await RunNoShowJobAsync();

        Assert.Equal(BookingStatus.NO_SHOW, (await RequireBookingAsync(order.BookingId)).Status);
        Assert.All(await ReadBookingCourtsAsync(order.BookingId), bc => Assert.False(bc.IsActive));
    }

    [Fact]
    public async Task NoShow_StillWithinTheGraceMinutes_IsUntouched()
    {
        var court = await SeedCourtAsync();
        var bookingId = await ConfirmedWalkInAsync(court);
        await MovePlayToJustStartedAsync(bookingId); // bắt đầu 5 phút trước, ân hạn 15 phút

        await RunNoShowJobAsync();

        Assert.Equal(BookingStatus.CONFIRMED, (await RequireBookingAsync(bookingId)).Status);
        Assert.All(await ReadBookingCourtsAsync(bookingId), bc => Assert.True(bc.IsActive));
    }

    [Fact]
    public async Task NoShow_CheckedInOrder_IsUntouched()
    {
        var court = await SeedCourtAsync();
        var bookingId = await InProgressAsync(court, InvoicePaymentStatus.PARTIALLY_PAID);
        await MovePlayAsync(bookingId, Tomorrow); // chưa hết giờ để Job-02 không đụng; Job-04 bỏ vì đã check-in

        await RunNoShowJobAsync();

        var booking = await RequireBookingAsync(bookingId);
        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
        Assert.NotNull(booking.CheckedInAt);
    }

    [Theory]
    [InlineData(CourtStatus.SUSPENDED)]
    [InlineData(CourtStatus.INACTIVE)]
    public async Task NoShow_CourtInSpecialStatus_KeepsItsStatus(CourtStatus special)
    {
        var court = await SeedCourtAsync();
        var bookingId = await ConfirmedWalkInAsync(court);
        await MovePlayAsync(bookingId, Yesterday);
        await SetCourtStatusAsync(court.CourtId, special);

        await RunNoShowJobAsync();

        Assert.Equal(BookingStatus.NO_SHOW, (await RequireBookingAsync(bookingId)).Status);
        Assert.Equal(special, (await ReadCourtAsync(court.CourtId)).Status);
    }

    [Fact]
    public async Task NoShow_CourtStillHeldByAnotherActiveOrder_IsNotReleased()
    {
        var court = await SeedCourtAsync();
        var noShowId = await ConfirmedWalkInAsync(court);
        await MovePlayAsync(noShowId, Yesterday);
        await BookPaidAsync(court); // đơn khác cùng sân, ba ngày sau, đang hiệu lực
        await SetCourtStatusAsync(court.CourtId, CourtStatus.IN_USE);

        await RunNoShowJobAsync();

        Assert.Equal(BookingStatus.NO_SHOW, (await RequireBookingAsync(noShowId)).Status);
        Assert.Equal(CourtStatus.IN_USE, (await ReadCourtAsync(court.CourtId)).Status);
    }

    [Fact]
    public async Task NoShow_NotifiesPeopleWaitingForTheSlot()
    {
        var court = await SeedCourtAsync();
        var bookingId = await ConfirmedWalkInAsync(court);
        await MovePlayAsync(bookingId, Yesterday);
        var waiter = $"cho-slot-{Guid.NewGuid():N}@test.local";
        await WithDbAsync(async db =>
        {
            db.SlotInterests.Add(new SlotInterest
            {
                Id = Guid.NewGuid(), CourtId = court.CourtId, Date = Yesterday, StartTime = PlayStart, EndTime = PlayEnd,
                Email = waiter, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            return await db.SaveChangesAsync();
        });
        var factory = await GetFactoryAsync();

        await RunNoShowJobAsync();

        var email = Assert.Single(factory.Emails.To(waiter));
        Assert.False(await HasSlotInterestAsync(court.CourtId)); // báo một lần rồi xóa
        // B3 (đổi đúng plan 6A-5): FE không có trang /booking (404); link dẫn về trang chủ như mọi đường nhả slot khác
        Assert.Contains("href=\"http://localhost:3000\"", email.Body);
        Assert.DoesNotContain("/booking?", email.Body);
    }

    // ───────────────────────── Job-02: kết thúc giờ chơi ─────────────────────────

    [Fact]
    public async Task Finish_InProgressEndedWithPaidInvoice_CompletesAndReleasesTheCourt()
    {
        var court = await SeedCourtAsync();
        var bookingId = await InProgressAsync(court, InvoicePaymentStatus.PAID);
        await MovePlayAsync(bookingId, Yesterday);
        await SetCourtStatusAsync(court.CourtId, CourtStatus.IN_USE);
        var invoiceBefore = await ReadInvoiceAsync(bookingId);

        await RunFinishJobAsync();

        Assert.Equal(BookingStatus.COMPLETED, (await RequireBookingAsync(bookingId)).Status);
        var invoice = await ReadInvoiceAsync(bookingId);
        Assert.Equal(InvoicePaymentStatus.PAID, invoice.PaymentStatus); // hóa đơn chỉ đổi updated_at
        Assert.True(invoice.UpdatedAt > invoiceBefore.UpdatedAt);
        Assert.Equal(invoiceBefore.FinalTotal, invoice.FinalTotal);
        Assert.All(await ReadBookingCourtsAsync(bookingId), bc => Assert.False(bc.IsActive));
        Assert.Equal(CourtStatus.AVAILABLE, (await ReadCourtAsync(court.CourtId)).Status);
    }

    [Theory]
    [InlineData(InvoicePaymentStatus.PARTIALLY_PAID)] // trả cọc online (IPN luôn để PARTIALLY_PAID)
    [InlineData(InvoicePaymentStatus.UNPAID)]
    public async Task Finish_InProgressEndedNotFullyPaid_WaitsForPaymentAtTheCounter(InvoicePaymentStatus invoiceStatus)
    {
        var court = await SeedCourtAsync();
        var bookingId = await InProgressAsync(court, invoiceStatus);
        await MovePlayAsync(bookingId, Yesterday);

        await RunFinishJobAsync();

        Assert.Equal(BookingStatus.PENDING_PAYMENT, (await RequireBookingAsync(bookingId)).Status);
        Assert.Equal(invoiceStatus, (await ReadInvoiceAsync(bookingId)).PaymentStatus);
        Assert.All(await ReadBookingCourtsAsync(bookingId), bc => Assert.False(bc.IsActive));
    }

    [Fact]
    public async Task Finish_ThenCheckout_CollectsExactlyWhatIsStillOwed()
    {
        // Đơn trả cọc online (PARTIALLY_PAID) hết giờ ⇒ chờ thu tiền; check-out sau đó chạy từ PENDING_PAYMENT và thu phần còn lại
        var court = await SeedCourtAsync();
        var order = await BookPendingAsync(court);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        await StartPlayingAsync(order.BookingId);
        await SetInvoiceStatusAsync(order.BookingId, InvoicePaymentStatus.PARTIALLY_PAID);
        await AddServiceFeeAsync(order.BookingId, 30_000m);
        await MovePlayAsync(order.BookingId, Yesterday);

        await RunFinishJobAsync();
        Assert.Equal(BookingStatus.PENDING_PAYMENT, (await RequireBookingAsync(order.BookingId)).Status);

        var call = await CheckoutAsync(await SeedStaffAsync(court.BranchId), order.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        var cash = Assert.Single(booking.Invoice.Payments, p => p.Method == PaymentTxMethod.CASH);
        Assert.Equal(30_000m, cash.Amount);
    }

    [Fact]
    public async Task Finish_InProgressNotEndedYet_IsUntouched()
    {
        var court = await SeedCourtAsync();
        var bookingId = await InProgressAsync(court, InvoicePaymentStatus.PAID);
        await MovePlayAsync(bookingId, Tomorrow);

        await RunFinishJobAsync();

        Assert.Equal(BookingStatus.IN_PROGRESS, (await RequireBookingAsync(bookingId)).Status);
        Assert.All(await ReadBookingCourtsAsync(bookingId), bc => Assert.True(bc.IsActive));
    }

    [Fact]
    public async Task Finish_ConfirmedEndedWithoutCheckIn_IsLeftForTheNoShowJob()
    {
        var court = await SeedCourtAsync();
        var bookingId = await ConfirmedWalkInAsync(court);
        await MovePlayAsync(bookingId, Yesterday);

        await RunFinishJobAsync();

        Assert.Equal(BookingStatus.CONFIRMED, (await RequireBookingAsync(bookingId)).Status);
    }

    [Fact]
    public async Task Finish_CourtStillHeldByAnotherActiveOrder_IsNotReleased()
    {
        var court = await SeedCourtAsync();
        var bookingId = await InProgressAsync(court, InvoicePaymentStatus.PAID);
        await MovePlayAsync(bookingId, Yesterday);
        await BookPaidAsync(court); // đơn khác cùng sân, ba ngày sau
        await SetCourtStatusAsync(court.CourtId, CourtStatus.IN_USE);

        await RunFinishJobAsync();

        Assert.Equal(BookingStatus.COMPLETED, (await RequireBookingAsync(bookingId)).Status);
        Assert.Equal(CourtStatus.IN_USE, (await ReadCourtAsync(court.CourtId)).Status);
    }

    [Fact]
    public async Task Finish_SuspendedCourt_KeepsItsStatus()
    {
        var court = await SeedCourtAsync();
        var bookingId = await InProgressAsync(court, InvoicePaymentStatus.PAID);
        await MovePlayAsync(bookingId, Yesterday);
        await SetCourtStatusAsync(court.CourtId, CourtStatus.SUSPENDED);

        await RunFinishJobAsync();

        Assert.Equal(BookingStatus.COMPLETED, (await RequireBookingAsync(bookingId)).Status);
        Assert.Equal(CourtStatus.SUSPENDED, (await ReadCourtAsync(court.CourtId)).Status);
    }

    [Fact]
    public async Task NoShow_SameCourtStillInUseByACheckedInOrder_StaysInUse()
    {
        // B6: "sân còn bận" tính sau khi job thắng, chỉ loại chính đơn NO_SHOW; đơn khác đang chơi trên sân giữ trạng thái IN_USE
        var court = await SeedCourtAsync();
        var noShowId = await ConfirmedWalkInAsync(court);
        await MovePlayAsync(noShowId, Yesterday);
        var playingId = await InProgressAsync(court, InvoicePaymentStatus.PARTIALLY_PAID);
        await SetCourtStatusAsync(court.CourtId, CourtStatus.IN_USE);

        await RunNoShowJobAsync();

        Assert.Equal(BookingStatus.NO_SHOW, (await RequireBookingAsync(noShowId)).Status);
        Assert.Equal(BookingStatus.IN_PROGRESS, (await RequireBookingAsync(playingId)).Status);
        Assert.Equal(CourtStatus.IN_USE, (await ReadCourtAsync(court.CourtId)).Status);
        Assert.All(await ReadBookingCourtsAsync(playingId), bc => Assert.True(bc.IsActive));
    }
}
