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
/// "Thanh toán cọc": số tiền = tiền cọc của hóa đơn (đơn cũ không có cọc: finalTotal), hạn theo PaymentHoldPolicy.TryForRetry,
/// tới hạn giữ chỗ ⇒ 400 PAYMENT_HOLD_EXPIRED; đơn đổi trạng thái giữa lúc đọc và lúc ghi ⇒ 409.
/// </summary>
[Trait("Category", TestCategories.Payment)]
[Trait("Catalog", "BE-IT-07")]
public class PaymentServiceRetryDepositTests
{
    private static readonly DateTime Created = PaymentServiceTestBuilder.DefaultNow; // 09:00 giờ VN

    private readonly PaymentServiceTestBuilder _builder = new();
    private readonly Guid _customerId = Guid.NewGuid();

    private PaymentService CreateService() => _builder.Build();

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
        _builder.Bookings.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        return booking;
    }

    // ── Số tiền ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovedDeposit_ChargesTheDepositNotTheTotal()
    {
        var booking = Booking(deposit: 60_000m);
        _builder.Time.Advance(TimeSpan.FromMinutes(2));

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal([60_000m], _builder.LinkAmounts);
        _builder.Payments.Verify(x => x.CreateAsync(It.Is<Payment>(p => p.Amount == 60_000m && p.Status == PaymentTxStatus.PENDING)), Times.Once);
        Assert.Equal(60_000m, result.DepositAmount);
        Assert.Equal(200_000m, result.FinalTotal);
    }

    [Fact]
    public async Task FullPaymentFallback_ChargesTheTotal()
    {
        var booking = Booking(deposit: 200_000m);

        await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal([200_000m], _builder.LinkAmounts);
    }

    [Fact]
    public async Task BookingBeforePhase6_WithoutDeposit_ChargesTheTotal()
    {
        var booking = Booking(deposit: null);

        await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal([200_000m], _builder.LinkAmounts);
    }

    // ── Hạn ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryNearTheEnd_ExtendsOnlyToTheHoldDeadline()
    {
        var booking = Booking();
        _builder.Time.Advance(TimeSpan.FromMinutes(8)); // 09:08: now + 10' = 09:18 vượt hạn giữ chỗ 09:15

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        var deadline = Created.AddMinutes(15);
        Assert.Equal(deadline, result.ExpiresAt);
        // Hạn mới ghi cho cả hóa đơn và slot lock bằng một câu cập nhật có điều kiện
        _builder.Bookings.Verify(x => x.ExtendPaymentWindowAsync(booking.Id, deadline, Created.AddMinutes(8)), Times.Once);
    }

    [Fact]
    public async Task ApprovedBooking_RetryTwoMinutesLater_KeepsTheApprovalDeadline()
    {
        // H-03 phần thanh toán lại: duyệt lúc phút 20 (hạn = hạn giữ chỗ = phút 35), thanh toán lại phút 22 ⇒ thành công, hạn vẫn phút 35
        var approvalDeadline = Created.AddMinutes(35);
        var booking = Booking(expiresAt: approvalDeadline, holdDeadline: approvalDeadline);
        _builder.Time.Advance(TimeSpan.FromMinutes(22));

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal(approvalDeadline, result.ExpiresAt);
        _builder.Bookings.Verify(x => x.ExtendPaymentWindowAsync(booking.Id, approvalDeadline, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task AtTheHoldDeadline_IsPaymentHoldExpiredWithoutVnPayOrPayment()
    {
        var booking = Booking(expiresAt: Created.AddMinutes(15));
        _builder.Time.Advance(TimeSpan.FromMinutes(15));

        var error = await Assert.ThrowsAsync<AppException>(() => CreateService().RetryPaymentAsync(booking.Id, _customerId));

        error.ShouldBeAppException(400, ErrorCodes.PaymentHoldExpired);
        Assert.Equal("Đã hết thời gian giữ chỗ, vui lòng đặt lại", error.Message);
        Assert.Empty(_builder.LinkAmounts);
        _builder.Payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task PaymentWindowPassedButHoldDeadlineNot_KeepsTheExistingExpiredError()
    {
        // VD hạn thanh toán bị rút ngắn (6A-4: thanh toán thất bại) rồi khách quay lại trước hạn giữ chỗ
        var booking = Booking(expiresAt: Created.AddMinutes(6));
        _builder.Time.Advance(TimeSpan.FromMinutes(7));

        var error = await Assert.ThrowsAsync<AppException>(() => CreateService().RetryPaymentAsync(booking.Id, _customerId));

        error.ShouldBeAppException(400, ErrorCodes.BadRequest);
        Assert.Empty(_builder.LinkAmounts);
    }

    [Fact]
    public async Task BookingBeforePhase6_WithoutHoldDeadline_UsesCreatedPlusFifteenMinutes()
    {
        var booking = Booking(deposit: null);
        booking.Invoice!.HoldDeadline = null;
        _builder.Time.Advance(TimeSpan.FromMinutes(8));

        var result = await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        Assert.Equal(Created.AddMinutes(15), result.ExpiresAt);
    }

    [Fact]
    public async Task BookingChangedBeforeTheWrite_ConflictWithoutNewLinkOrPayment()
    {
        // Đơn vừa bị hủy hoặc vừa được trả bằng link cũ: không kéo hạn, không tạo link mới
        var booking = Booking();
        _builder.Bookings.Setup(x => x.ExtendPaymentWindowAsync(booking.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(false);

        var error = await Assert.ThrowsAsync<AppException>(() => CreateService().RetryPaymentAsync(booking.Id, _customerId));

        error.ShouldBeAppException(409, ErrorCodes.Conflict);
        Assert.Empty(_builder.LinkAmounts);
        _builder.Payments.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        _builder.Payments.Verify(x => x.FailPendingByInvoiceAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Retry_VoidsOpenLinksAndUsesTheSameGatewayAsBefore()
    {
        var booking = Booking();
        booking.Invoice!.Payments.Add(TestDataFactory.CreatePayment(amount: 60_000m, status: PaymentTxStatus.PENDING,
            method: PaymentTxMethod.VNPAY));

        await CreateService().RetryPaymentAsync(booking.Id, _customerId);

        _builder.Payments.Verify(x => x.FailPendingByInvoiceAsync(booking.Invoice.Id, null, Created), Times.Once);
        _builder.Payments.Verify(x => x.CreateAsync(It.Is<Payment>(p =>
            p.InvoiceId == booking.Invoice.Id && p.Method == PaymentTxMethod.VNPAY && p.TransactionRef == "RETRY-1")), Times.Once);
    }
}
