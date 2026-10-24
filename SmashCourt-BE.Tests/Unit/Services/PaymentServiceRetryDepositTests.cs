using SmashCourt_BE.Services.Deposit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Payment;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// "Thanh toán cọc" sau khi được duyệt (kéo từ 6A-4 về 6A-3, chốt 24/10; spec 7.8 dòng 1, API doc 3.3): số tiền VNPay = tiền cọc của hóa đơn
/// (đơn trước Phase 6: finalTotal), hạn theo PaymentHoldPolicy.TryForRetry, tới hạn giữ chỗ ⇒ 400 PAYMENT_HOLD_EXPIRED.
/// </summary>
[Trait("Category", TestCategories.Payment)]
[Trait("Catalog", "BE-IT-07")]
public class PaymentServiceRetryDepositTests
{
    private static readonly DateTime Created = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc); // 09:00 giờ VN

    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<ISlotLockRepository> _slotLocks = new();
    private readonly Mock<IVnPayService> _vnPay = new();
    private readonly FakeTimeProvider _time = new(Created);
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly List<decimal> _vnPayAmounts = [];

    public PaymentServiceRetryDepositTests()
    {
        _vnPay.Setup(x => x.CreatePaymentUrl(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()))
            .Returns((string _, decimal amount, string _) =>
            {
                _vnPayAmounts.Add(amount);
                return new VnPayPaymentUrlResult { Url = "https://vnpay.test/retry", TransactionRef = "RETRY" };
            });
    }

    private PaymentService CreateService()
    {
        var settings = new Mock<IOptionsMonitor<BookingPaymentSettings>>();
        settings.Setup(x => x.CurrentValue).Returns(new BookingPaymentSettings());
        return new PaymentService(
            _payments.Object, _bookings.Object, Mock.Of<IInvoiceRepository>(), _slotLocks.Object, Mock.Of<ICourtRepository>(),
            _vnPay.Object, new EmailService(TestConfigurationFactory.Create()),
            new PromotionEngineService(Mock.Of<IPromotionRepository>()), Mock.Of<ILogger<PaymentService>>(),
            TestConfigurationFactory.Create(), Mock.Of<IBroadcastService>(), settings.Object, _time);
    }

    private Booking Booking(decimal finalTotal = 200_000m, decimal? deposit = 60_000m, DateTime? expiresAt = null, DateTime? holdDeadline = null)
    {
        var booking = TestDataFactory.CreateBooking(customerId: _customerId, status: BookingStatus.PENDING);
        booking.CreatedAt = Created;
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, finalTotal: finalTotal, expiresAt: expiresAt ?? Created.AddMinutes(10));
        booking.Invoice.HoldDeadline = holdDeadline ?? Created.AddMinutes(15);
        if (deposit is { } amount)
        {
            booking.Invoice.DepositAmount = amount;
            booking.Invoice.DepositRate = finalTotal == 0 ? 0 : amount / finalTotal;
            booking.Invoice.DepositSource = DepositSource.MANAGER;
        }
        _bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        return booking;
    }

    // ── Số tiền ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovedDeposit_ChargesTheDepositNotTheTotal()
    {
        var booking = Booking(deposit: 60_000m);
        _time.Advance(TimeSpan.FromMinutes(2));

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal([60_000m], _vnPayAmounts);
        _payments.Verify(x => x.CreateAsync(It.Is<Payment>(p => p.Amount == 60_000m && p.Status == PaymentTxStatus.PENDING)), Times.Once);
        Assert.Equal(60_000m, result.DepositAmount);
        Assert.Equal(200_000m, result.FinalTotal);
    }

    [Fact]
    public async Task FullPaymentFallback_ChargesTheTotal()
    {
        var booking = Booking(deposit: 200_000m);

        await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal([200_000m], _vnPayAmounts);
    }

    [Fact]
    public async Task BookingBeforePhase6_WithoutDeposit_ChargesTheTotal()
    {
        var booking = Booking(deposit: null);

        await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal([200_000m], _vnPayAmounts);
    }

    // ── Hạn ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryNearTheEnd_ExtendsOnlyToTheHoldDeadline()
    {
        var booking = Booking();
        _time.Advance(TimeSpan.FromMinutes(8)); // 09:08: now + 10' = 09:18 vượt hạn giữ chỗ 09:15

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        var deadline = Created.AddMinutes(15);
        Assert.Equal(deadline, booking.Invoice!.ExpiresAt);
        Assert.Equal(deadline, booking.Invoice.HoldDeadline);
        Assert.Equal(deadline, result.ExpiresAt);
        _slotLocks.Verify(x => x.UpdateExpiryByBookingIdAsync(booking.Id, deadline), Times.Once);
    }

    [Fact]
    public async Task ApprovedBooking_RetryTwoMinutesLater_KeepsTheApprovalDeadline()
    {
        // H-03 phần thanh toán lại: duyệt lúc phút 20 (hạn = hạn giữ chỗ = phút 35), thanh toán lại phút 22 ⇒ thành công, hạn vẫn phút 35
        var approvalDeadline = Created.AddMinutes(35);
        var booking = Booking(expiresAt: approvalDeadline, holdDeadline: approvalDeadline);
        _time.Advance(TimeSpan.FromMinutes(22));

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal(approvalDeadline, result.ExpiresAt);
        Assert.Equal(approvalDeadline, booking.Invoice!.ExpiresAt);
    }

    [Fact]
    public async Task AtTheHoldDeadline_IsPaymentHoldExpiredWithoutVnPayOrPayment()
    {
        var booking = Booking(expiresAt: Created.AddMinutes(15));
        _time.Advance(TimeSpan.FromMinutes(15));

        var error = await Assert.ThrowsAsync<AppException>(() => CreateService().RetryPaymentAsync(booking.Id, _customerId));

        error.ShouldBeAppException(400, ErrorCodes.PaymentHoldExpired);
        Assert.Equal("Đã hết thời gian giữ chỗ, vui lòng đặt lại", error.Message);
        Assert.Empty(_vnPayAmounts);
        _payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task PaymentWindowPassedButHoldDeadlineNot_KeepsTheExistingExpiredError()
    {
        // VD hạn thanh toán bị rút ngắn (6A-4: thanh toán thất bại) rồi khách quay lại trước hạn giữ chỗ
        var booking = Booking(expiresAt: Created.AddMinutes(6));
        _time.Advance(TimeSpan.FromMinutes(7));

        var error = await Assert.ThrowsAsync<AppException>(() => CreateService().RetryPaymentAsync(booking.Id, _customerId));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest);
        Assert.Empty(_vnPayAmounts);
    }

    [Fact]
    public async Task BookingBeforePhase6_WithoutHoldDeadline_UsesCreatedPlusFifteenMinutes()
    {
        var booking = Booking(deposit: null);
        booking.Invoice!.HoldDeadline = null;
        _time.Advance(TimeSpan.FromMinutes(8));

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal(Created.AddMinutes(15), result.ExpiresAt);
    }
}
