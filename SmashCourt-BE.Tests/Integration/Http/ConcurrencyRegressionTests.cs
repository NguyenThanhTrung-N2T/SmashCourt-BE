using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Hai thao tác cùng lúc trên cùng dữ liệu, qua hai kết nối DB thật, sau khi mọi transaction chuyển sang Read Committed kèm câu cập nhật
/// có điều kiện: bên thua nhận 409/400 thay vì 500, và không bao giờ mất tiền (tiền vào đơn hủy luôn có Refund, không thu trùng).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class ConcurrencyRegressionTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private const int Rounds = 4;

    private static void AssertNoServerError(params HttpStatusCode[] statuses) =>
        Assert.All(statuses, s => Assert.True((int)s < 500, $"Không được trả {s}"));

    [Fact]
    public async Task StaffCancelRacingThePaymentResult_NeverLeavesMoneyOnACancelledOrderWithoutRefund()
    {
        // Mốc 24 giờ hoàn 100 %: đơn ba ngày sau, đã trả rồi mới hủy cũng được hoàn đủ
        await EnsureCancelPoliciesAsync();
        for (var round = 0; round < Rounds; round++)
        {
            var order = await BookPendingAsync();
            var staff = await SeedStaffAsync(order.Court.BranchId);

            var (cancel, ipn) = await (StaffCancelAsync(staff, order.BookingId), IpnAsync(order.TransactionRef, order.Amount)).WhenBoth();

            AssertNoServerError(cancel.Status, ipn);
            var booking = await RequireBookingAsync(order.BookingId);
            var paid = booking.Invoice!.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount);
            Assert.Equal(order.Amount, paid);
            if (booking.Status == BookingStatus.PAID_ONLINE)
                Assert.Equal(HttpStatusCode.Conflict, cancel.Status);
            else
            {
                Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
                Assert.Equal(paid, (await ReadRefundsAsync(order.BookingId)).Sum(r => r.Amount));
            }
        }
    }

    [Fact]
    public async Task TwoStaffCancelAtOnce_OnlyOneCancels()
    {
        var order = await BookPendingAsync();
        var first = await SeedStaffAsync(order.Court.BranchId);
        var second = await SeedStaffAsync(order.Court.BranchId);

        var (a, b) = await (StaffCancelAsync(first, order.BookingId), StaffCancelAsync(second, order.BookingId)).WhenBoth();

        AssertNoServerError(a.Status, b.Status);
        Assert.Single(new[] { a.Status, b.Status }, s => s == HttpStatusCode.OK);
        Assert.Equal(BookingStatus.CANCELLED, (await RequireBookingAsync(order.BookingId)).Status);
    }

    [Fact]
    public async Task CustomerAndStaffCancelAtOnce_OnlyOneCancels()
    {
        var order = await BookPendingAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);

        var (customer, staffCall) = await (CustomerCancelAsync(order.Customer, order.BookingId), StaffCancelAsync(staff, order.BookingId)).WhenBoth();

        AssertNoServerError(customer.Status, staffCall.Status);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Contains(booking.CancelReason, new BookingCancelReason?[] { BookingCancelReason.CUSTOMER_CANCELLED, BookingCancelReason.STAFF_CANCELLED });
    }

    [Fact]
    public async Task TwoCheckoutsAtOnce_CollectOnce()
    {
        var order = await BookDepositPaidAsync();
        await StartPlayingAsync(order.BookingId);
        var staff = await SeedStaffAsync(order.Court.BranchId);

        var (a, b) = await (CheckoutAsync(staff, order.BookingId), CheckoutAsync(staff, order.BookingId)).WhenBoth();

        AssertNoServerError(a.Status, b.Status);
        Assert.Single(new[] { a.Status, b.Status }, s => s == HttpStatusCode.OK);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(140_000m, Assert.Single(booking.Invoice!.Payments, p => p.Method == PaymentTxMethod.CASH).Amount);
    }

    [Fact]
    public async Task TwoCheckInsAtOnce_OnlyOneStartsPlay()
    {
        var order = await BookPaidAsync();
        await MovePlayToJustStartedAsync(order.BookingId);
        var staff = await SeedStaffAsync(order.Court.BranchId);
        var client = await CreateClientForAsync(staff);

        var (a, b) = await (
            SendAsync(client.PostAsync($"/api/bookings/{order.BookingId}/check-in", null)),
            SendAsync(client.PostAsync($"/api/bookings/{order.BookingId}/check-in", null))).WhenBoth();

        AssertNoServerError(a.Status, b.Status);
        Assert.Single(new[] { a.Status, b.Status }, s => s == HttpStatusCode.OK);
        Assert.Equal(BookingStatus.IN_PROGRESS, (await RequireBookingAsync(order.BookingId)).Status);
    }

    [Fact]
    public async Task TwoConfirmRefundsAtOnce_RefundOnce()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPaidAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));

        var (a, b) = await (ConfirmRefundAsync(staff, order.BookingId), ConfirmRefundAsync(staff, order.BookingId)).WhenBoth();

        AssertNoServerError(a.Status, b.Status);
        Assert.Single(new[] { a.Status, b.Status }, s => s == HttpStatusCode.OK);
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_REFUNDED, booking.Status);
        Assert.Equal(order.Amount, Assert.Single(booking.Invoice!.Payments).RefundedAmount);
    }

    [Fact]
    public async Task TwoWalkInsForTheSameSlot_OneWinsTheOtherGetsSlotUnavailable()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var court = await SeedCourtAsync(200_000m);
            var staff = await SeedStaffAsync(court.BranchId);
            var client = await CreateClientForAsync(staff);
            var body = new
            {
                bookingDate = DateTimeHelper.GetTodayInVietnam().AddDays(2).ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ss"),
                courts = new[] { new { courtId = court.CourtId, startTime = "08:00:00", endTime = "09:00:00" } },
                guestName = "Khách",
                guestPhone = "0900000000"
            };

            var (a, b) = await (
                SendAsync(client.PostAsJsonAsync("/api/bookings/walk-in", body)),
                SendAsync(client.PostAsJsonAsync("/api/bookings/walk-in", body))).WhenBoth();

            AssertNoServerError(a.Status, b.Status);
            Assert.Single(new[] { a.Status, b.Status }, s => s == HttpStatusCode.Created);
            Assert.Contains(new[] { a, b }, c => c.Status == HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task AddServiceRacingCheckout_NeverLeavesAnUncollectedServiceOnAPaidInvoice()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var order = await BookPaidAsync();
            await StartPlayingAsync(order.BookingId);
            var serviceId = await SeedBranchServiceAsync(order.Court.BranchId, 15_000m);
            var staff = await SeedStaffAsync(order.Court.BranchId);
            var client = await CreateClientForAsync(staff);

            var (add, checkout) = await (
                SendAsync(client.PostAsJsonAsync($"/api/bookings/{order.BookingId}/services", new { serviceId, quantity = 2 })),
                CheckoutAsync(staff, order.BookingId)).WhenBoth();

            AssertNoServerError(add.Status, checkout.Status);
            await AssertApiStatusAsync(HttpStatusCode.OK, checkout);
            var booking = await RequireBookingAsync(order.BookingId);
            var collected = booking.Invoice!.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount);
            Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice.PaymentStatus);
            // Dịch vụ vào trước check-out thì được thu; vào sau thì bị từ chối. Không có trường hợp thứ ba
            Assert.Equal(booking.Invoice.FinalTotal, collected);
            // POST dịch vụ thành công trả 201 (trước 05/11 so với 200 nên vòng "dịch vụ thắng" luôn bị chấm sai)
            Assert.Equal(add.Status == HttpStatusCode.Created ? 30_000m : 0m, booking.Invoice.ServiceFee);
        }
    }

    // Vòng đua trên một cách tất định (CI 04/11 run 41892105829, lượt AddServiceRacingCheckout đỏ: tổng 230000, thu 200000): request check-out
    // nạp đơn trước, dịch vụ commit sau đó, rồi check-out mới lấy khóa. Hóa đơn đã nằm trong DbContext của request check-out từ lần nạp đầu,
    // nên lần "đọc lại" trong transaction phải lấy giá trị mới từ DB chứ không phải object EF đang theo dõi
    [Fact]
    public async Task Checkout_WhenServiceIsCommittedAfterTheOrderWasLoaded_CollectsTheNewTotal()
    {
        var order = await BookPaidAsync();
        await StartPlayingAsync(order.BookingId);
        var serviceId = await SeedBranchServiceAsync(order.Court.BranchId, 15_000m);
        var staff = await SeedStaffAsync(order.Court.BranchId);
        var factory = await GetFactoryAsync();
        using var checkoutRequest = factory.Services.CreateScope();
        await checkoutRequest.ServiceProvider.GetRequiredService<IBookingRepository>().GetByIdWithDetailsAsync(order.BookingId);

        var add = await (await CreateClientForAsync(staff)).PostAsJsonAsync(
            $"/api/bookings/{order.BookingId}/services", new { serviceId, quantity = 2 });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        await checkoutRequest.ServiceProvider.GetRequiredService<IBookingService>()
            .CheckoutAsync(order.BookingId, staff.Id, staff.Role.ToString());

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        Assert.Equal(30_000m, booking.Invoice.ServiceFee);
        Assert.Equal(booking.Invoice.FinalTotal, booking.Invoice.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount));
    }

    private Task<Guid> SeedBranchServiceAsync(Guid branchId, decimal price) =>
        WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var service = new Service
            {
                Id = Guid.NewGuid(), Name = $"Nước {Guid.NewGuid():N}"[..16], Unit = "chai", DefaultPrice = price,
                Status = ServiceStatus.ACTIVE, CreatedAt = now, UpdatedAt = now
            };
            db.Services.Add(service);
            db.BranchServices.Add(new BranchService
            {
                Id = Guid.NewGuid(), BranchId = branchId, ServiceId = service.Id, Price = price,
                Status = BranchServiceStatus.ENABLED, CreatedAt = now, UpdatedAt = now
            });
            await db.SaveChangesAsync();
            return service.Id;
        });
}

internal static class ConcurrentCalls
{
    /// <summary>Chạy hai lời gọi cùng lúc trên hai luồng và trả cả hai kết quả.</summary>
    public static async Task<(TA, TB)> WhenBoth<TA, TB>(this (Task<TA> First, Task<TB> Second) calls)
    {
        await Task.WhenAll(calls.First, calls.Second);
        return (calls.First.Result, calls.Second.Result);
    }
}
