using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Thêm/xóa dịch vụ đua với nhau và với check-out trên cùng một đơn (06/11, sau N-38). Thứ tự đua dựng tất định bằng
/// <see cref="BookingRowLockGate"/>: cả hai request nạp đơn xong rồi mới lần lượt lấy khóa, đúng khe hở thật giữa lúc nạp và lúc khóa.
/// Mỗi test HTTP còn chạy bộ kiểm SC6 (gồm SC6-7: hóa đơn PAID thu đủ) trên các đơn đã chạm.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class ServiceChangeConcurrencyTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private const decimal Price = 15_000m;

    private sealed record Playing(Order Order, Guid ServiceId, HttpClient First, HttpClient Second);

    // Đơn đã trả đủ 200 000 đ online, đang chơi; một dịch vụ 15 000 đ ở chi nhánh; hai nhân viên của chi nhánh
    private async Task<Playing> PlayingOrderAsync()
    {
        var order = await BookPaidAsync();
        await StartPlayingAsync(order.BookingId);
        var serviceId = await SeedBranchServiceAsync(order.Court.BranchId, Price);
        var first = await CreateClientForAsync(await SeedStaffAsync(order.Court.BranchId));
        var second = await CreateClientForAsync(await SeedStaffAsync(order.Court.BranchId));
        return new Playing(order, serviceId, first, second);
    }

    private static Task<ApiCall> AddAsync(HttpClient client, Guid bookingId, Guid serviceId, int quantity) =>
        SendAsync(client.PostAsJsonAsync($"/api/bookings/{bookingId}/services", new { serviceId, quantity }));

    private static Task<ApiCall> RemoveAsync(HttpClient client, Guid bookingId, Guid serviceId) =>
        SendAsync(client.DeleteAsync($"/api/bookings/{bookingId}/services/{serviceId}"));

    private static Task<ApiCall> CheckoutWithAsync(HttpClient client, Guid bookingId) =>
        SendAsync(client.PostAsync($"/api/bookings/{bookingId}/checkout", null));

    private Task<List<(Guid ServiceId, int Quantity)>> ReadLinesAsync(Guid bookingId) =>
        WithDbAsync(async db => (await db.BookingServices.AsNoTracking().Where(s => s.BookingId == bookingId)
            .Select(s => new { s.ServiceId, s.Quantity }).ToListAsync()).Select(s => (s.ServiceId, s.Quantity)).ToList());

    private static int QuantityIn(ApiCall call, Guid serviceId) =>
        call.Data.GetProperty("services").EnumerateArray()
            .Single(s => s.GetProperty("serviceId").GetGuid() == serviceId).GetProperty("quantity").GetInt32();

    // first nạp đơn rồi chờ khóa trước, second sau; nhả khóa thì first chạy xong rồi second mới lấy được khóa
    private async Task<(ApiCall First, ApiCall Second)> RaceOnTheBookingAsync(
        Guid bookingId, Func<Task<ApiCall>> first, Func<Task<ApiCall>> second)
    {
        var factory = await GetFactoryAsync();
        await using var gate = await BookingRowLockGate.HoldAsync(factory.Services, bookingId);
        var a = first();
        await gate.WaitForBlockedAsync(1, a);
        var b = second();
        await gate.WaitForBlockedAsync(2, a, b);
        await gate.ReleaseAsync();
        return (await a, await b);
    }

    private async Task AssertInvoiceAsync(Guid bookingId, decimal serviceFee)
    {
        var invoice = (await RequireBookingAsync(bookingId)).Invoice!;
        Assert.Equal(serviceFee, invoice.ServiceFee);
        Assert.Equal(invoice.CourtFee - invoice.LoyaltyDiscountAmount - invoice.PromotionDiscountAmount + serviceFee, invoice.FinalTotal);
    }

    [Fact]
    public async Task AddingAServiceAlreadyOnTheOrder_ReturnsTheMergedQuantity()
    {
        var p = await PlayingOrderAsync();
        await AssertApiStatusAsync(HttpStatusCode.Created, await AddAsync(p.First, p.Order.BookingId, p.ServiceId, 2));

        var second = await AddAsync(p.First, p.Order.BookingId, p.ServiceId, 3);

        await AssertApiStatusAsync(HttpStatusCode.Created, second);
        // Phản hồi (cũng là payload SignalR BookingUpdated) phải là số lượng sau khi cộng, không phải số lượng lúc nạp đơn
        Assert.Equal(5, QuantityIn(second, p.ServiceId));
        Assert.Equal([(p.ServiceId, 5)], await ReadLinesAsync(p.Order.BookingId));
        await AssertInvoiceAsync(p.Order.BookingId, 5 * Price);
    }

    [Fact]
    public async Task TwoStaffAddTheSameNewServiceAtOnce_KeepOneLineWithBothQuantities()
    {
        var p = await PlayingOrderAsync();

        var (a, b) = await RaceOnTheBookingAsync(p.Order.BookingId,
            () => AddAsync(p.First, p.Order.BookingId, p.ServiceId, 2),
            () => AddAsync(p.Second, p.Order.BookingId, p.ServiceId, 2));

        await AssertApiStatusAsync(HttpStatusCode.Created, a);
        await AssertApiStatusAsync(HttpStatusCode.Created, b);
        Assert.Equal([(p.ServiceId, 4)], await ReadLinesAsync(p.Order.BookingId));
        Assert.Equal(4, QuantityIn(b, p.ServiceId));
        await AssertInvoiceAsync(p.Order.BookingId, 4 * Price);
    }

    [Fact]
    public async Task TwoStaffRemoveTheSameServiceAtOnce_BothSucceedAndTheServiceIsGone()
    {
        var p = await PlayingOrderAsync();
        await AssertApiStatusAsync(HttpStatusCode.Created, await AddAsync(p.First, p.Order.BookingId, p.ServiceId, 2));

        var (a, b) = await RaceOnTheBookingAsync(p.Order.BookingId,
            () => RemoveAsync(p.First, p.Order.BookingId, p.ServiceId),
            () => RemoveAsync(p.Second, p.Order.BookingId, p.ServiceId));

        // Xóa là idempotent (xóa lần hai trả thành công): khi hai lần xóa đua nhau cũng vậy, không được 500
        await AssertApiStatusAsync(HttpStatusCode.OK, a);
        await AssertApiStatusAsync(HttpStatusCode.OK, b);
        Assert.Empty(await ReadLinesAsync(p.Order.BookingId));
        Assert.Equal(JsonValueKind.Array, b.Data.GetProperty("services").ValueKind);
        Assert.Empty(b.Data.GetProperty("services").EnumerateArray());
        await AssertInvoiceAsync(p.Order.BookingId, 0m);
    }

    [Fact]
    public async Task AddRacingRemoveOfTheSameService_TheAcceptedAddIsKept()
    {
        var p = await PlayingOrderAsync();
        await AssertApiStatusAsync(HttpStatusCode.Created, await AddAsync(p.First, p.Order.BookingId, p.ServiceId, 2));

        var (remove, add) = await RaceOnTheBookingAsync(p.Order.BookingId,
            () => RemoveAsync(p.First, p.Order.BookingId, p.ServiceId),
            () => AddAsync(p.Second, p.Order.BookingId, p.ServiceId, 3));

        await AssertApiStatusAsync(HttpStatusCode.OK, remove);
        await AssertApiStatusAsync(HttpStatusCode.Created, add);
        // Dòng cũ đã bị xóa trước; lần thêm được báo thành công thì 3 phần phải còn trên đơn và được tính tiền
        Assert.Equal([(p.ServiceId, 3)], await ReadLinesAsync(p.Order.BookingId));
        Assert.Equal(3, QuantityIn(add, p.ServiceId));
        await AssertInvoiceAsync(p.Order.BookingId, 3 * Price);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoveServiceRacingCheckout_CollectsExactlyTheFinalTotal(bool removeFirst)
    {
        var p = await PlayingOrderAsync();
        await AssertApiStatusAsync(HttpStatusCode.Created, await AddAsync(p.First, p.Order.BookingId, p.ServiceId, 2));
        Func<Task<ApiCall>> remove = () => RemoveAsync(p.First, p.Order.BookingId, p.ServiceId);
        Func<Task<ApiCall>> checkout = () => CheckoutWithAsync(p.Second, p.Order.BookingId);

        var (first, second) = await RaceOnTheBookingAsync(p.Order.BookingId, removeFirst ? remove : checkout, removeFirst ? checkout : remove);
        var (removeCall, checkoutCall) = removeFirst ? (first, second) : (second, first);

        await AssertApiStatusAsync(HttpStatusCode.OK, checkoutCall);
        // Check-out trước thì đơn đã COMPLETED, xóa dịch vụ bị từ chối (400), không được 500
        await AssertApiStatusAsync(removeFirst ? HttpStatusCode.OK : HttpStatusCode.BadRequest, removeCall);
        var booking = await RequireBookingAsync(p.Order.BookingId);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        Assert.Equal(removeFirst ? 0m : 2 * Price, booking.Invoice.ServiceFee);
        Assert.Equal(booking.Invoice.FinalTotal,
            booking.Invoice.Payments.Where(x => x.Status == PaymentTxStatus.SUCCESS).Sum(x => x.Amount - x.RefundedAmount));
    }

    // Cặp với Checkout_WhenServiceIsCommittedAfterTheOrderWasLoaded (N-38) theo chiều ngược: dịch vụ bị xóa sau khi check-out đã nạp đơn
    // thì check-out không được thu theo tổng cũ (thu thừa tiền dịch vụ đã xóa)
    [Fact]
    public async Task Checkout_WhenServiceIsRemovedAfterTheOrderWasLoaded_DoesNotOvercharge()
    {
        var p = await PlayingOrderAsync();
        await AssertApiStatusAsync(HttpStatusCode.Created, await AddAsync(p.First, p.Order.BookingId, p.ServiceId, 2));
        var staff = await SeedStaffAsync(p.Order.Court.BranchId);
        var factory = await GetFactoryAsync();
        using var checkoutRequest = factory.Services.CreateScope();
        await checkoutRequest.ServiceProvider.GetRequiredService<IBookingRepository>().GetByIdWithDetailsAsync(p.Order.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, await RemoveAsync(p.First, p.Order.BookingId, p.ServiceId));
        await checkoutRequest.ServiceProvider.GetRequiredService<IBookingService>()
            .CheckoutAsync(p.Order.BookingId, staff.Id, staff.Role.ToString());

        var booking = await RequireBookingAsync(p.Order.BookingId);
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        Assert.Equal(0m, booking.Invoice.ServiceFee);
        Assert.Equal(p.Order.Amount, booking.Invoice.FinalTotal);
        Assert.DoesNotContain(booking.Invoice.Payments, x => x.Method == PaymentTxMethod.CASH);
    }
}
