using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Payment;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

public class PaymentServiceTests
{
    [Fact]
    public async Task HandleVnPayIpnAsync_WhenSignatureIsInvalid_LogsAndDoesNotMutateState()
    {
        var payments = new Mock<IPaymentRepository>();
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-TAMPERED";
        var isSuccess = true;
        var rawPayload = "tampered-payload";
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(false);
        payments.Setup(x => x.GetByTransactionRefAsync(transactionRef)).ReturnsAsync((Payment?)null);

        await CreateService(payments: payments, vnPay: vnPay).HandleVnPayIpnAsync(
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            new DefaultHttpContext().Request);

        payments.Verify(x => x.CreateIpnLogAsync(It.Is<PaymentIpnLog>(log =>
            log.Provider == IpnProvider.VNPAY &&
            log.ProviderTransactionId == transactionRef &&
            !log.IsValid)), Times.Once);
        payments.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Never);
        payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task HandleVnPayIpnAsync_WhenPaymentSucceeds_FinalizesPaymentAndConfirmsBooking()
    {
        var payments = new Mock<IPaymentRepository>();
        var bookings = new Mock<IBookingRepository>();
        var invoices = new Mock<IInvoiceRepository>();
        var slots = new Mock<ISlotLockRepository>();
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-IPN-SUCCESS";
        var isSuccess = true;
        var rawPayload = "payload";
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PENDING);
        var branch = new Branch { Id = booking.BranchId, Name = "Test branch" };
        var court = TestDataFactory.CreateCourt(booking.BranchId, "Court 1");
        booking.Branch = branch;
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id,
            CourtId = court.Id,
            Date = booking.BookingDate,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 30),
            Court = court,
            IsActive = true
        });
        var invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: 250_000m);
        invoice.Booking = booking;
        var payment = TestDataFactory.CreatePayment(
            invoice.Id, 250_000m, PaymentTxStatus.PENDING, PaymentTxMethod.VNPAY);
        payment.TransactionRef = transactionRef;
        payment.Invoice = invoice;
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(true);
        payments.Setup(x => x.GetByTransactionRefAsync(transactionRef)).ReturnsAsync(payment);
        bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                booking.Id, BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(1);

        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_ResponseCode"] = "00"
        });
        await CreateService(payments, bookings, vnPay, slots, invoices).HandleVnPayIpnAsync(
            query, new DefaultHttpContext().Request);

        Assert.Equal(PaymentTxStatus.SUCCESS, payment.Status);
        Assert.Equal(InvoicePaymentStatus.PARTIALLY_PAID, invoice.PaymentStatus);
        payments.Verify(x => x.UpdateAsync(payment), Times.Once);
        invoices.Verify(x => x.UpdateAsync(invoice), Times.Once);
        slots.Verify(x => x.DeleteByBookingIdAsync(booking.Id), Times.Once);
        bookings.Verify(x => x.AtomicUpdatePaymentSuccessAsync(
            booking.Id, BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task HandleVnPayIpnAsync_WhenPaymentFails_CancelsBookingAndReleasesSlot()
    {
        var payments = new Mock<IPaymentRepository>();
        var bookings = new Mock<IBookingRepository>();
        var invoices = new Mock<IInvoiceRepository>();
        var slots = new Mock<ISlotLockRepository>();
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-IPN-FAILED";
        var isSuccess = false;
        var rawPayload = "payload";
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PENDING);
        var invoice = TestDataFactory.CreateInvoice(booking.Id);
        invoice.Booking = booking;
        var payment = TestDataFactory.CreatePayment(
            invoice.Id, status: PaymentTxStatus.PENDING, method: PaymentTxMethod.VNPAY);
        payment.TransactionRef = transactionRef;
        payment.Invoice = invoice;
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(true);
        payments.Setup(x => x.GetByTransactionRefAsync(transactionRef)).ReturnsAsync(payment);

        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_ResponseCode"] = "24"
        });
        await CreateService(payments, bookings, vnPay, slots, invoices).HandleVnPayIpnAsync(
            query, new DefaultHttpContext().Request);

        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(PaymentTxStatus.FAILED, payment.Status);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, invoice.PaymentStatus);
        bookings.Verify(x => x.UpdateAsync(booking), Times.Once);
        bookings.Verify(x => x.UpdateCourtActiveStatusAsync(booking.Id, false), Times.Once);
        invoices.Verify(x => x.UpdateAsync(invoice), Times.Once);
        slots.Verify(x => x.DeleteByBookingIdAsync(booking.Id), Times.Once);
    }

    [Fact]
    public async Task HandleVnPayIpnAsync_WhenBookingIsAlreadyFinalized_SkipsProcessing()
    {
        var payments = new Mock<IPaymentRepository>();
        var bookings = new Mock<IBookingRepository>();
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-IPN-DUPLICATE";
        var isSuccess = true;
        var rawPayload = "payload";
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PAID_ONLINE);
        var invoice = TestDataFactory.CreateInvoice(booking.Id, paymentStatus: InvoicePaymentStatus.PARTIALLY_PAID);
        invoice.Booking = booking;
        var payment = TestDataFactory.CreatePayment(invoice.Id, status: PaymentTxStatus.SUCCESS);
        payment.TransactionRef = transactionRef;
        payment.Invoice = invoice;
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(true);
        payments.Setup(x => x.GetByTransactionRefAsync(transactionRef)).ReturnsAsync(payment);

        await CreateService(payments, bookings, vnPay).HandleVnPayIpnAsync(
            new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()),
            new DefaultHttpContext().Request);

        payments.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Never);
        bookings.Verify(x => x.AtomicUpdatePaymentSuccessAsync(
            It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task HandleVnPayIpnAsync_WhenAmountDoesNotMatch_RejectsWithoutMutatingPayment()
    {
        var payments = new Mock<IPaymentRepository>();
        var bookings = new Mock<IBookingRepository>();
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-AMOUNT-MISMATCH";
        var isSuccess = true;
        var rawPayload = "payload";
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PENDING);
        var invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: 200_000m);
        invoice.Booking = booking;
        var payment = TestDataFactory.CreatePayment(
            invoice.Id, 200_000m, PaymentTxStatus.PENDING, PaymentTxMethod.VNPAY);
        payment.TransactionRef = transactionRef;
        payment.Invoice = invoice;
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(true);
        payments.Setup(x => x.GetByTransactionRefAsync(transactionRef)).ReturnsAsync(payment);

        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = "10000000"
        });
        await CreateService(payments: payments, bookings: bookings, vnPay: vnPay)
            .HandleVnPayIpnAsync(query, new DefaultHttpContext().Request);

        Assert.Equal(PaymentTxStatus.PENDING, payment.Status);
        bookings.Verify(x => x.AtomicUpdatePaymentSuccessAsync(
            It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        payments.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task HandleVnPayReturnAsync_WhenSignatureIsInvalid_ReturnsFailureWithoutReadingPayment()
    {
        var payments = new Mock<IPaymentRepository>();
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-INVALID";
        var isSuccess = false;
        var rawPayload = "payload";
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(false);

        var result = await CreateService(payments: payments, vnPay: vnPay)
            .HandleVnPayReturnAsync(new QueryCollection(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()));

        Assert.False(result.IsSuccess);
        Assert.Equal("97", result.ResponseCode);
        Assert.Equal("Chữ ký không hợp lệ", result.Message);
        payments.Verify(x => x.GetByTransactionRefAsync(
            It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task HandleVnPayReturnAsync_WhenPaymentSucceeds_ReturnsConfirmedBookingResult()
    {
        var payments = new Mock<IPaymentRepository>();
        var bookings = new Mock<IBookingRepository>();
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-SUCCESS";
        var isSuccess = true;
        var rawPayload = "payload";
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PAID_ONLINE);
        booking.BookingCode = "BK-001";
        var invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: 250_000m);
        invoice.Booking = booking;
        var payment = TestDataFactory.CreatePayment(amount: 250_000m);
        payment.Invoice = invoice;
        payment.TransactionRef = transactionRef;
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(true);
        payments.Setup(x => x.GetByTransactionRefAsync(transactionRef, true)).ReturnsAsync(payment);
        bookings.Setup(x => x.GetBookingStatusAsync(booking.Id)).ReturnsAsync(BookingStatus.PAID_ONLINE);

        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_ResponseCode"] = "00",
            ["vnp_Amount"] = "25000000"
        });
        var result = await CreateService(payments: payments, bookings: bookings, vnPay: vnPay)
            .HandleVnPayReturnAsync(query);

        Assert.True(result.IsSuccess);
        Assert.Equal(booking.Id.ToString(), result.BookingId);
        Assert.Equal("BK-001", result.BookingCode);
        Assert.Equal(250_000m, result.Amount);
        Assert.Contains("đã được xác nhận", result.Message);
    }

    [Fact]
    public async Task HandleVnPayReturnAsync_WhenPaymentIsCancelled_ReturnsFailureMessage()
    {
        var vnPay = new Mock<IVnPayService>();
        var transactionRef = "TX-CANCELLED";
        var isSuccess = false;
        var rawPayload = "payload";
        vnPay.Setup(x => x.VerifyIpn(
                It.IsAny<IQueryCollection>(), out transactionRef, out isSuccess, out rawPayload))
            .Returns(true);

        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["vnp_TxnRef"] = transactionRef,
            ["vnp_ResponseCode"] = "24"
        });
        var result = await CreateService(vnPay: vnPay).HandleVnPayReturnAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal("24", result.ResponseCode);
        Assert.Contains("hủy", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetryPaymentAsync_MissingBookingThrowsNotFound()
    {
        var bookings = new Mock<IBookingRepository>();
        bookings.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>())).ReturnsAsync((Booking?)null);
        var promotions = new Mock<IPromotionRepository>();
        var service = new PaymentService(Mock.Of<IPaymentRepository>(), bookings.Object, Mock.Of<IInvoiceRepository>(), Mock.Of<ISlotLockRepository>(), Mock.Of<ICourtRepository>(), Mock.Of<IVnPayService>(), new EmailService(TestConfigurationFactory.Create()), new PromotionEngineService(promotions.Object), Mock.Of<ILogger<PaymentService>>(), TestConfigurationFactory.Create(), Mock.Of<IBroadcastService>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.RetryPaymentAsync(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task RetryPaymentAsync_WhenCustomerDoesNotOwnBooking_ThrowsForbiddenWithoutCreatingPayment()
    {
        var bookings = new Mock<IBookingRepository>();
        var payments = new Mock<IPaymentRepository>();
        var booking = TestDataFactory.CreateBooking(customerId: Guid.NewGuid(), status: BookingStatus.PENDING);
        booking.Invoice = TestDataFactory.CreateInvoice(
            booking.Id,
            expiresAt: DateTime.UtcNow.AddMinutes(5));
        bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        var service = CreateService(payments, bookings);

        var exception = await Assert.ThrowsAsync<AppException>(() => service.RetryPaymentAsync(
            booking.Id, Guid.NewGuid()));

        exception.ShouldBeAppException(403, ErrorCodes.Forbidden);
        payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task RetryPaymentAsync_WhenInvoiceHasExpired_ThrowsBadRequestWithoutCallingVnPay()
    {
        var bookings = new Mock<IBookingRepository>();
        var vnPay = new Mock<IVnPayService>();
        var customerId = Guid.NewGuid();
        var booking = TestDataFactory.CreateBooking(customerId: customerId, status: BookingStatus.PENDING);
        booking.Invoice = TestDataFactory.CreateInvoice(
            booking.Id,
            expiresAt: DateTime.UtcNow.AddMinutes(-1));
        bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        var service = CreateService(bookings: bookings, vnPay: vnPay);

        var exception = await Assert.ThrowsAsync<AppException>(() => service.RetryPaymentAsync(
            booking.Id, customerId));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "hết hạn");
        vnPay.Verify(x => x.CreatePaymentUrl(
            It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RetryPaymentAsync_WhenPendingPaymentExists_VoidsOldPaymentCreatesNewPaymentAndExtendsExpiry()
    {
        var bookings = new Mock<IBookingRepository>();
        var payments = new Mock<IPaymentRepository>();
        var slots = new Mock<ISlotLockRepository>();
        var vnPay = new Mock<IVnPayService>();
        var customerId = Guid.NewGuid();
        var oldPayment = TestDataFactory.CreatePayment(amount: 300_000m, status: PaymentTxStatus.PENDING);
        var booking = TestDataFactory.CreateBooking(customerId: customerId, status: BookingStatus.PENDING);
        var invoice = TestDataFactory.CreateInvoice(
            booking.Id,
            finalTotal: 300_000m,
            expiresAt: DateTime.UtcNow.AddMinutes(5),
            payments: [oldPayment]);
        booking.Invoice = invoice;
        bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        vnPay.Setup(x => x.CreatePaymentUrl(
                booking.Id.ToString(), 300_000m, It.IsAny<string>()))
            .Returns(new VnPayPaymentUrlResult
            {
                Url = "https://vnpay.test/retry",
                TransactionRef = "RETRY-TRANSACTION"
            });
        var service = CreateService(payments, bookings, vnPay, slots);

        var result = await service.RetryPaymentAsync(booking.Id, customerId);

        Assert.Equal(booking.Id, result.BookingId);
        Assert.Equal("https://vnpay.test/retry", result.PaymentUrl);
        Assert.Equal(300_000m, result.FinalTotal);
        Assert.Equal(PaymentTxStatus.FAILED, oldPayment.Status);
        Assert.True(invoice.ExpiresAt > DateTime.UtcNow.AddMinutes(9));
        payments.Verify(x => x.UpdateAsync(It.Is<Payment>(payment =>
            payment.Id == oldPayment.Id && payment.Status == PaymentTxStatus.FAILED)), Times.Once);
        payments.Verify(x => x.CreateAsync(It.Is<Payment>(payment =>
            payment.InvoiceId == invoice.Id &&
            payment.Amount == 300_000m &&
            payment.Status == PaymentTxStatus.PENDING &&
            payment.TransactionRef == "RETRY-TRANSACTION")), Times.Once);
        bookings.Verify(x => x.UpdateAsync(booking), Times.Once);
        slots.Verify(x => x.UpdateExpiryByBookingIdAsync(
            booking.Id, It.Is<DateTime>(expiry => expiry > DateTime.UtcNow.AddMinutes(9))), Times.Once);
    }

    private static PaymentService CreateService(
        Mock<IPaymentRepository>? payments = null,
        Mock<IBookingRepository>? bookings = null,
        Mock<IVnPayService>? vnPay = null,
        Mock<ISlotLockRepository>? slots = null,
        Mock<IInvoiceRepository>? invoices = null)
    {
        var promotionRepository = new Mock<IPromotionRepository>();
        return new PaymentService(
            (payments ?? new Mock<IPaymentRepository>()).Object,
            (bookings ?? new Mock<IBookingRepository>()).Object,
            (invoices ?? new Mock<IInvoiceRepository>()).Object,
            (slots ?? new Mock<ISlotLockRepository>()).Object,
            Mock.Of<ICourtRepository>(),
            (vnPay ?? new Mock<IVnPayService>()).Object,
            new EmailService(TestConfigurationFactory.Create()),
            new PromotionEngineService(promotionRepository.Object),
            Mock.Of<ILogger<PaymentService>>(),
            TestConfigurationFactory.Create(),
            Mock.Of<IBroadcastService>());
    }
}
