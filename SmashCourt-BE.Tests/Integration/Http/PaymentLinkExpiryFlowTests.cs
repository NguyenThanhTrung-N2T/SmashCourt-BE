using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Hạn link thanh toán (P6-27, plan 6A-6 Task 3–4), chạy đúng Program.cs trên schema thật: URL VNPay mang vnp_ExpireDate sớm hơn hạn
/// thanh toán của đơn 2 phút, hai mốc theo GMT+7; thanh toán lại còn dưới 3 phút ⇒ 400 PAYMENT_HOLD_EXPIRED, không đổi gì; cổng lỗi khi
/// tạo link ⇒ 500 và transaction rollback (R27).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Payment")]
[Trait("Catalog", "BE-UT-08")]
public sealed class PaymentLinkExpiryFlowTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private static readonly TimeSpan Buffer = TimeSpan.FromMinutes(2);

    private static Dictionary<string, string> UrlQuery(string paymentUrl) =>
        QueryHelpers.ParseQuery(new Uri(paymentUrl).Query).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    // Cùng định dạng mà response của BE dùng cho mọi DateTime (giờ VN, dd/MM/yyyy HH:mm:ss)
    private static string VietnamText(DateTime utc) =>
        DateTimeHelper.ToVietnamTime(utc).ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    private Task MoveDeadlinesAsync(Guid bookingId, DateTime expiresAt, DateTime holdDeadline) =>
        WithDbAsync(db => db.Invoices.Where(i => i.BookingId == bookingId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ExpiresAt, (DateTime?)expiresAt)
                .SetProperty(i => i.HoldDeadline, (DateTime?)holdDeadline)));

    [Fact]
    public async Task NewBooking_LinkExpiresTwoMinutesBeforeThePaymentDeadline()
    {
        var court = await SeedCourtAsync(200_000m);
        var before = DateTime.UtcNow;

        var (call, _) = await BookAsync(court);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        var booking = await RequireBookingAsync(call.BookingId);
        var linkExpiresAt = booking.Invoice!.ExpiresAt!.Value - Buffer;
        var query = UrlQuery(call.DataString("paymentUrl")!);
        Assert.Equal(VnPayHelper.FormatVietnamTime(linkExpiresAt), query["vnp_ExpireDate"]);
        Assert.Equal(VietnamText(linkExpiresAt), call.DataString("paymentLinkExpiresAt"));

        // vnp_CreateDate là giờ VN lúc tạo link (không còn UTC như VNPAY.NET, T-5)
        var created = DateTime.ParseExact(query["vnp_CreateDate"], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var vietnamBefore = DateTimeHelper.ToVietnamTime(before).AddSeconds(-1);
        Assert.InRange(created, vietnamBefore, DateTimeHelper.ToVietnamTime(DateTime.UtcNow));

        // Mã giao dịch do BE sinh, là mã IPN dùng để tìm Payment
        var payment = Assert.Single(booking.Invoice.Payments);
        Assert.Equal(payment.TransactionRef, query["vnp_TxnRef"]);
        Assert.Matches("^[0-9a-f]{32}$", payment.TransactionRef);
    }

    [Fact]
    public async Task NewBooking_TheSignedLinkIsPaidThroughIpnAsBefore()
    {
        // Hồi quy: IPN của giao dịch có mã BE sinh vẫn xác nhận đơn
        var order = await BookPendingAsync();

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));

        Assert.Equal(BookingStatus.PAID_ONLINE, (await RequireBookingAsync(order.BookingId)).Status);
    }

    [Fact]
    public async Task RetryWithFourMinutesLeft_LinkEndsTwoMinutesBeforeTheHoldDeadline()
    {
        var order = await BookPendingAsync();
        var deadline = DateTime.UtcNow.AddMinutes(4);
        await MoveDeadlinesAsync(order.BookingId, deadline, deadline);

        var retry = await RetryAsync(order.Customer, order.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, retry);
        var booking = await RequireBookingAsync(order.BookingId);
        // Hạn mới bị chặn ở hạn giữ chỗ; link hết hạn trước đó 2 phút
        var expiresAt = booking.Invoice!.ExpiresAt!.Value;
        Assert.Equal(VnPayHelper.FormatVietnamTime(expiresAt - Buffer), UrlQuery(retry.DataString("paymentUrl")!)["vnp_ExpireDate"]);
        Assert.Equal(VietnamText(expiresAt - Buffer), retry.DataString("paymentLinkExpiresAt"));
        Assert.Single(booking.Invoice.Payments, p => p.Status == PaymentTxStatus.PENDING && p.TransactionRef != order.TransactionRef);
    }

    [Theory]
    [InlineData(150)] // 2'30"
    [InlineData(30)]
    public async Task RetryWithLessThanThreeMinutesLeft_PaymentHoldExpiredAndNothingChanges(int secondsLeft)
    {
        var order = await BookPendingAsync();
        var deadline = DateTime.UtcNow.AddSeconds(secondsLeft);
        await MoveDeadlinesAsync(order.BookingId, deadline, deadline);
        var before = await RequireBookingAsync(order.BookingId);
        var locksBefore = (await ReadLocksAsync(order.BookingId)).Select(l => l.ExpiresAt).Order().ToList();

        var retry = await RetryAsync(order.Customer, order.BookingId);

        Assert.Equal(HttpStatusCode.BadRequest, retry.Status);
        Assert.Equal(ErrorCodes.PaymentHoldExpired, retry.Code);
        var after = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PENDING, after.Status);
        Assert.Equal(before.Invoice!.ExpiresAt, after.Invoice!.ExpiresAt);
        var payment = Assert.Single(after.Invoice.Payments);
        Assert.Equal(PaymentTxStatus.PENDING, payment.Status);
        Assert.Equal(order.TransactionRef, payment.TransactionRef);
        Assert.Equal(locksBefore, (await ReadLocksAsync(order.BookingId)).Select(l => l.ExpiresAt).Order().ToList());
    }

    [Fact]
    public async Task GatewayFailsWhileCreatingTheLink_InternalErrorAndTheRetryIsRolledBack()
    {
        // R27: lỗi của cổng khi tạo link (ở đây: thiếu mã terminal) lan ra 500 INTERNAL_ERROR; transaction thanh toán lại rollback nên
        // không gia hạn, không hủy Payment cũ, không có Payment mới
        var order = await BookPendingAsync();
        var before = await RequireBookingAsync(order.BookingId);
        var factory = await GetFactoryAsync();
        ApiCall retry;
        factory.SetSetting("VnPay:TmnCode", "");
        try
        {
            retry = await RetryAsync(order.Customer, order.BookingId);
        }
        finally
        {
            factory.SetSetting("VnPay:TmnCode", null);
        }

        Assert.Equal(HttpStatusCode.InternalServerError, retry.Status);
        Assert.Equal(ErrorCodes.InternalError, retry.Code);
        var after = await RequireBookingAsync(order.BookingId);
        Assert.Equal(before.Invoice!.ExpiresAt, after.Invoice!.ExpiresAt);
        var payment = Assert.Single(after.Invoice.Payments);
        Assert.Equal(PaymentTxStatus.PENDING, payment.Status);
        Assert.Equal(order.TransactionRef, payment.TransactionRef);

        // Cấu hình trả lại ⇒ thanh toán lại bình thường
        await AssertApiStatusAsync(HttpStatusCode.OK, await RetryAsync(order.Customer, order.BookingId));
    }
}
