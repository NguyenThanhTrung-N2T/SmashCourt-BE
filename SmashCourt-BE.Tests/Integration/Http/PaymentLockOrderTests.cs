using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Repositories;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Thứ tự khóa giữa kết quả thanh toán và lệnh hủy, trên hai kết nối DB thật: mọi luồng khóa dòng booking trước dòng Payment.
/// Nếu kết quả thanh toán khóa Payment trước rồi mới chờ booking, còn lệnh hủy giữ booking rồi chờ Payment, PostgreSQL phát hiện
/// deadlock và hủy một bên (500, hoặc mất lần ghi nhận tiền).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Payment")]
[Trait("Catalog", "BE-IT-22")]
public sealed class PaymentLockOrderTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    [Fact]
    public async Task PaymentResultArrivingWhileACancelHoldsTheBooking_WaitsInsteadOfDeadlocking()
    {
        var order = await BookPendingAsync();
        var invoiceId = (await RequireBookingAsync(order.BookingId)).Invoice!.Id;
        var factory = await GetFactoryAsync();
        var now = DateTime.UtcNow;

        // Lệnh hủy: đã thắng câu cập nhật có điều kiện trên booking, chưa đụng tới Payment
        using var cancelScope = factory.Services.CreateScope();
        var cancelDb = cancelScope.ServiceProvider.GetRequiredService<SmashCourtContext>();
        await using var cancel = await cancelDb.Database.BeginTransactionAsync();
        Assert.Equal(1, await new BookingRepository(cancelDb).CancelWithStatusCheckAsync(
            order.BookingId, [BookingStatus.PENDING], BookingStatus.CANCELLED, CancelSourceEnum.STAFF,
            BookingCancelReason.STAFF_CANCELLED, null, now));
        // Như BookingCancellationService: nhả sân và slot lock trong cùng transaction (SC6-3)
        await new BookingRepository(cancelDb).UpdateCourtActiveStatusAsync(order.BookingId, false);
        await new SlotLockRepository(cancelDb).DeleteByBookingIdAsync(order.BookingId);

        // Kết quả thanh toán đến đúng lúc đó
        var ipn = Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPaymentResultService>().ApplyAsync(
                new GatewayPaymentResult(IpnProvider.VNPAY, order.TransactionRef, true, true, order.Amount, "00", "raw"));
        });
        await Task.Delay(800);

        // Lệnh hủy đi tiếp: đánh FAILED Payment còn chờ rồi commit
        await new PaymentRepository(cancelDb).FailPendingByInvoiceAsync(invoiceId, null, now);
        await cancel.CommitAsync();
        await ipn;

        // Tiền về sau khi đơn đã hủy: ghi nhận đủ và hoàn 100 % tại chi nhánh
        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
        Assert.Equal(PaymentTxStatus.SUCCESS, Assert.Single(booking.Invoice!.Payments).Status);
        Assert.Equal(order.Amount, Assert.Single(await ReadRefundsAsync(order.BookingId)).Amount);
    }
}
