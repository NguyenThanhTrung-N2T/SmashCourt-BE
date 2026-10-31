using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Phần riêng của từng endpoint thanh toán: IPN chỉ chuyển kết quả của cổng cho lõi chung, trình duyệt quay lại chỉ đọc,
/// thanh toán lại tạo link mới qua cổng. Nghiệp vụ của kết quả giao dịch ở PaymentResultServiceTests.
/// </summary>
[Trait("Category", TestCategories.Payment)]
public class PaymentServiceTests
{
    private static QueryCollection Query(params (string Key, string Value)[] values) =>
        new(values.ToDictionary(v => v.Key, v => new StringValues(v.Value)));

    private static GatewayPaymentResult Result(string transactionRef, bool signatureValid, bool success, decimal? amount, string code) =>
        new(IpnProvider.VNPAY, transactionRef, signatureValid, success, amount, code, "payload");

    // ───────────────────────── IPN ─────────────────────────

    [Fact]
    public async Task Ipn_HandsTheGatewayResultToTheSharedPaymentCore()
    {
        var builder = new PaymentServiceTestBuilder();
        var query = Query(("vnp_TxnRef", "TX-1"));
        var result = Result("TX-1", true, true, 60_000m, "00");
        builder.Gateway.Setup(x => x.ReadResult(query)).Returns(result);

        await builder.Build().HandleVnPayIpnAsync(query, new DefaultHttpContext().Request);

        builder.PaymentResults.Verify(x => x.ApplyAsync(result), Times.Once);
    }

    // ───────────────────────── Trình duyệt quay lại ─────────────────────────

    [Fact]
    public async Task Return_InvalidSignature_FailsWithoutReadingPayment()
    {
        var builder = new PaymentServiceTestBuilder();
        builder.Gateway.Setup(x => x.ReadResult(It.IsAny<IQueryCollection>())).Returns(Result("TX-INVALID", false, false, null, "00"));

        var result = await builder.Build().HandleVnPayReturnAsync(Query());

        Assert.False(result.IsSuccess);
        Assert.Equal("97", result.ResponseCode);
        Assert.Equal("Chữ ký không hợp lệ", result.Message);
        builder.Payments.Verify(x => x.GetByTransactionRefAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Return_PaidBooking_ReadsTheFreshStatusWithoutWriting()
    {
        var builder = new PaymentServiceTestBuilder();
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PAID_ONLINE);
        booking.BookingCode = "BK-001";
        var invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: 250_000m);
        invoice.Booking = booking;
        var payment = TestDataFactory.CreatePayment(amount: 250_000m);
        payment.Invoice = invoice;
        builder.Gateway.Setup(x => x.ReadResult(It.IsAny<IQueryCollection>())).Returns(Result("TX-SUCCESS", true, true, 250_000m, "00"));
        builder.Payments.Setup(x => x.GetByTransactionRefAsync("TX-SUCCESS", true)).ReturnsAsync(payment);
        builder.Bookings.Setup(x => x.GetBookingStatusAsync(booking.Id)).ReturnsAsync(BookingStatus.PAID_ONLINE);

        var result = await builder.Build().HandleVnPayReturnAsync(Query());

        Assert.True(result.IsSuccess);
        Assert.Equal(booking.Id.ToString(), result.BookingId);
        Assert.Equal("BK-001", result.BookingCode);
        Assert.Equal(250_000m, result.Amount);
        Assert.Contains("đã được xác nhận", result.Message);
        builder.PaymentResults.Verify(x => x.ApplyAsync(It.IsAny<GatewayPaymentResult>()), Times.Never);
    }

    [Fact]
    public async Task Return_CustomerCancelledAtTheGateway_ShowsTheGatewayMessage()
    {
        var builder = new PaymentServiceTestBuilder();
        builder.Gateway.Setup(x => x.ReadResult(It.IsAny<IQueryCollection>())).Returns(Result("TX-CANCELLED", true, false, 60_000m, "24"));

        var result = await builder.Build().HandleVnPayReturnAsync(Query());

        Assert.False(result.IsSuccess);
        Assert.Equal("24", result.ResponseCode);
        Assert.Contains("hủy", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ───────────── Trình duyệt quay lại: câu theo trạng thái đơn (N-36, P6-28; plan 6A-6 Task 5) ─────────────

    private const string ConfirmedMessage = "Thanh toán thành công! Đặt sân của bạn đã được xác nhận.";
    private const string ConfirmingMessage = "Đã nhận kết quả thanh toán, hệ thống đang xác nhận đơn đặt sân.";
    private const string RefundMessage =
        "Đã nhận thanh toán nhưng đơn đặt sân đã hết thời gian giữ chỗ trước khi tiền về. Khoản tiền này sẽ được hoàn đủ tại chi nhánh.";
    private const string CancelledMessage =
        "Đơn đặt sân đã bị hủy. Hệ thống đang đối soát giao dịch này, vui lòng xem chi tiết trong lịch sử đặt sân.";
    private const string CheckHistoryMessage = "Đã nhận kết quả thanh toán, vui lòng xem trạng thái trong lịch sử đặt sân.";

    // Payment của giao dịch quay về (đọc lại từ DB) kèm trạng thái đơn đọc lại từ DB
    private static PaymentServiceTestBuilder ReturnOf(
        BookingStatus? bookingStatus, PaymentTxStatus paymentStatus, bool gatewaySuccess = true, string code = "00")
    {
        var builder = new PaymentServiceTestBuilder();
        var booking = TestDataFactory.CreateBooking(status: bookingStatus ?? BookingStatus.PENDING);
        var invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: 120_000m);
        invoice.Booking = booking;
        var payment = TestDataFactory.CreatePayment(amount: 120_000m, status: paymentStatus);
        payment.Invoice = invoice;
        builder.Gateway.Setup(x => x.ReadResult(It.IsAny<IQueryCollection>()))
            .Returns(Result("TX-RETURN", true, gatewaySuccess, 120_000m, code));
        builder.Payments.Setup(x => x.GetByTransactionRefAsync("TX-RETURN", true)).ReturnsAsync(payment);
        builder.Bookings.Setup(x => x.GetBookingStatusAsync(booking.Id)).ReturnsAsync(bookingStatus ?? BookingStatus.PENDING);
        return builder;
    }

    [Theory]
    [Trait("Catalog", "BE-UT-13")]
    [InlineData(BookingStatus.PAID_ONLINE, PaymentTxStatus.SUCCESS, ConfirmedMessage)]
    [InlineData(BookingStatus.CONFIRMED, PaymentTxStatus.SUCCESS, ConfirmedMessage)]
    [InlineData(BookingStatus.IN_PROGRESS, PaymentTxStatus.SUCCESS, ConfirmedMessage)]
    [InlineData(BookingStatus.PENDING_PAYMENT, PaymentTxStatus.SUCCESS, ConfirmedMessage)]
    [InlineData(BookingStatus.COMPLETED, PaymentTxStatus.SUCCESS, ConfirmedMessage)]
    [InlineData(BookingStatus.PENDING, PaymentTxStatus.PENDING, ConfirmingMessage)]       // IPN chưa về
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND, PaymentTxStatus.SUCCESS, RefundMessage)] // P6-07 đã ghi khoản này kèm Refund
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND, PaymentTxStatus.FAILED, CancelledMessage)] // chờ hoàn khoản khác, khoản này chưa ghi
    [InlineData(BookingStatus.CANCELLED, PaymentTxStatus.FAILED, CancelledMessage)]        // Job-01 đã hủy, IPN của khoản này chưa về
    [InlineData(BookingStatus.CANCELLED_REFUNDED, PaymentTxStatus.SUCCESS, CancelledMessage)] // có thể đã hoàn xong: không hứa hoàn
    [InlineData(BookingStatus.NO_SHOW, PaymentTxStatus.SUCCESS, CheckHistoryMessage)]
    [InlineData(BookingStatus.PENDING_APPROVAL, PaymentTxStatus.PENDING, CheckHistoryMessage)]
    public async Task Return_GatewaySuccess_MessageFollowsTheBookingAndThisPayment(
        BookingStatus bookingStatus, PaymentTxStatus paymentStatus, string expectedMessage)
    {
        var builder = ReturnOf(bookingStatus, paymentStatus);

        var result = await builder.Build().HandleVnPayReturnAsync(Query());

        Assert.True(result.IsSuccess); // giữ nghĩa: cổng báo giao dịch thành công
        Assert.Equal(bookingStatus.ToString(), result.BookingStatus);
        Assert.Equal(expectedMessage, result.Message);
        builder.PaymentResults.Verify(x => x.ApplyAsync(It.IsAny<GatewayPaymentResult>()), Times.Never);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-13")]
    public async Task Return_RefundPromiseOnlyForCancelledStatusesFromTheSharedClassification()
    {
        // Mọi trạng thái hủy (BookingStatusTransition) đều không bao giờ nhận câu "đã xác nhận"
        foreach (var status in SmashCourt_BE.Helpers.BookingStatusTransition.GetCancelledStatuses())
        {
            var result = await ReturnOf(status, PaymentTxStatus.SUCCESS).Build().HandleVnPayReturnAsync(Query());

            Assert.NotEqual(ConfirmedMessage, result.Message);
            Assert.Equal(status == BookingStatus.CANCELLED_PENDING_REFUND ? RefundMessage : CancelledMessage, result.Message);
        }
    }

    [Fact]
    [Trait("Catalog", "BE-UT-13")]
    public async Task Return_PaymentNotFound_NoBookingStatusAndAskToCheckHistory()
    {
        var builder = new PaymentServiceTestBuilder();
        builder.Gateway.Setup(x => x.ReadResult(It.IsAny<IQueryCollection>())).Returns(Result("TX-UNKNOWN", true, true, 50_000m, "00"));

        var result = await builder.Build().HandleVnPayReturnAsync(Query());

        Assert.True(result.IsSuccess);
        Assert.Null(result.BookingStatus);
        Assert.Null(result.BookingId);
        Assert.Equal(CheckHistoryMessage, result.Message);
    }

    [Theory]
    [Trait("Catalog", "BE-UT-13")]
    [InlineData("24", BookingStatus.PENDING, "Khách hàng hủy giao dịch")]
    [InlineData("15", BookingStatus.PENDING, "Giao dịch đã quá thời gian chờ thanh toán, vui lòng tạo lại link thanh toán")]
    [InlineData("79", BookingStatus.CANCELLED, "Giao dịch vượt quá số lần nhập sai mật khẩu")]
    public async Task Return_GatewayFailure_GatewayMessageAndStillTheBookingStatus(string code, BookingStatus status, string expected)
    {
        var result = await ReturnOf(status, PaymentTxStatus.FAILED, gatewaySuccess: false, code: code).Build()
            .HandleVnPayReturnAsync(Query());

        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.ResponseCode);
        Assert.Equal(expected, result.Message);
        Assert.Equal(status.ToString(), result.BookingStatus);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-13")]
    public async Task Return_InvalidSignature_HasNoBookingStatus()
    {
        var builder = new PaymentServiceTestBuilder();
        builder.Gateway.Setup(x => x.ReadResult(It.IsAny<IQueryCollection>())).Returns(Result("TX-INVALID", false, true, null, "00"));

        var result = await builder.Build().HandleVnPayReturnAsync(Query());

        Assert.Null(result.BookingStatus);
    }

    // ───────────────────────── Thanh toán lại ─────────────────────────

    [Fact]
    public async Task Retry_MissingBooking_NotFound()
    {
        var builder = new PaymentServiceTestBuilder();

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().RetryPaymentAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, error.ErrorCode);
    }

    [Fact]
    public async Task Retry_NotTheOwner_ForbiddenWithoutPayment()
    {
        var builder = new PaymentServiceTestBuilder();
        var booking = TestDataFactory.CreateBooking(customerId: Guid.NewGuid(), status: BookingStatus.PENDING);
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, expiresAt: PaymentServiceTestBuilder.DefaultNow.AddMinutes(5));
        builder.Bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().RetryPaymentAsync(booking.Id, Guid.NewGuid()));

        error.ShouldBeAppException(403, ErrorCodes.Forbidden);
        builder.Payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    [InlineData(BookingStatus.CANCELLED)]
    public async Task Retry_BookingNotWaitingForPayment_BadRequest(BookingStatus status)
    {
        var builder = new PaymentServiceTestBuilder();
        var customerId = Guid.NewGuid();
        var booking = TestDataFactory.CreateBooking(customerId: customerId, status: status);
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, expiresAt: PaymentServiceTestBuilder.DefaultNow.AddMinutes(5));
        builder.Bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);

        var error = await Assert.ThrowsAsync<AppException>(() => builder.Build().RetryPaymentAsync(booking.Id, customerId));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest, "chờ thanh toán");
        Assert.Empty(builder.LinkAmounts);
    }

    [Fact]
    public async Task Retry_OldBookingWithoutDeposit_ChargesTheTotalWithNewLink()
    {
        var builder = new PaymentServiceTestBuilder();
        var customerId = Guid.NewGuid();
        var booking = TestDataFactory.CreateBooking(customerId: customerId, status: BookingStatus.PENDING);
        booking.CreatedAt = PaymentServiceTestBuilder.DefaultNow;
        var invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: 300_000m,
            expiresAt: PaymentServiceTestBuilder.DefaultNow.AddMinutes(5),
            payments: [TestDataFactory.CreatePayment(amount: 300_000m, status: PaymentTxStatus.PENDING, method: PaymentTxMethod.VNPAY)]);
        booking.Invoice = invoice;
        builder.Bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);

        var result = await builder.Build().RetryPaymentAsync(booking.Id, customerId);

        Assert.Equal("https://vnpay.test/retry", result.PaymentUrl);
        Assert.Equal(300_000m, result.FinalTotal);
        Assert.Equal("PENDING", result.Status);
        Assert.Null(result.DepositAmount);
        Assert.Equal([300_000m], builder.LinkAmounts);
        builder.Payments.Verify(x => x.CreateAsync(It.Is<Payment>(p =>
            p.InvoiceId == invoice.Id && p.Amount == 300_000m && p.Status == PaymentTxStatus.PENDING)), Times.Once);
    }
}
