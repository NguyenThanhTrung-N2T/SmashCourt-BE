using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Tests.Integration.Http;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Email xác nhận hủy (sửa 25/10): hoàn 0 đ có hai lý do khác nhau. Đơn đã có tiền về mà hoàn 0 đ là do hủy quá gần giờ chơi; đơn chưa
/// trả đồng nào thì "chưa thanh toán nên không phát sinh hoàn tiền". Mã cũ ghi "hủy quá gần giờ chơi" cho cả đơn chưa trả hủy trước nhiều ngày.
/// Hoàn tiền luôn bằng tiền mặt tại chi nhánh đặt sân.
/// </summary>
[Trait("Category", TestCategories.Email)]
[Trait("Catalog", "BE-IT-23")]
public class CancelEmailTests
{
    private const string NothingPaidText = "Đơn chưa được thanh toán nên không phát sinh khoản hoàn tiền nào.";
    private const string TooLateText = "Đơn này không được hoàn tiền do hủy quá gần giờ chơi.";
    private const string RefundAtBranchText = "Cách nhận tiền hoàn";

    private readonly RecordingEmailService _email = new(TestConfigurationFactory.Create());

    // ── EmailService: nội dung ba nhánh ───────────────────────────────────

    [Fact]
    public async Task SendCancelConfirmation_NothingPaid_SaysNotPaidInsteadOfTooLate()
    {
        await SendAsync(refundAmount: 0m, hasPaidAmount: false);

        var body = SingleBody();
        Assert.Contains(NothingPaidText, body);
        Assert.DoesNotContain(TooLateText, body);
        Assert.DoesNotContain(RefundAtBranchText, body);
    }

    [Fact]
    public async Task SendCancelConfirmation_PaidButNoRefund_SaysTooLate()
    {
        await SendAsync(refundAmount: 0m, hasPaidAmount: true);

        var body = SingleBody();
        Assert.Contains(TooLateText, body);
        Assert.DoesNotContain(NothingPaidText, body);
        Assert.DoesNotContain(RefundAtBranchText, body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendCancelConfirmation_WithRefund_ShowsAmountAndBranch_RegardlessOfFlag(bool hasPaidAmount)
    {
        await SendAsync(refundAmount: 60_000m, hasPaidAmount: hasPaidAmount);

        var body = SingleBody();
        Assert.Contains(RefundAtBranchText, body);
        Assert.Contains(60_000m.ToString("N0") + " VNĐ", body);
        Assert.Contains("SmashCourt Thủ Đức", body);
        Assert.DoesNotContain(TooLateText, body);
        Assert.DoesNotContain(NothingPaidText, body);
    }

    private Task SendAsync(decimal refundAmount, bool hasPaidAmount) =>
        _email.SendCancelConfirmationAsync("khach@test.local", "Nguyễn Văn An", Guid.NewGuid(),
            "SmashCourt Thủ Đức", "1 Võ Văn Ngân", "0281234567", refundAmount, hasPaidAmount);

    private string SingleBody()
    {
        var email = Assert.Single(_email.Sent);
        Assert.Equal("khach@test.local", email.To);
        Assert.Contains("Xác nhận hủy đặt sân", email.Subject);
        return email.Body;
    }

    // ── BookingNotificationService: chọn lý do theo Payment của đơn ───────

    [Fact]
    public async Task NotifyCancelled_OrderWithoutPayments_SaysNothingPaid()
    {
        await NotifyCancelledAsync(OrderWith(), refundAmount: 0m);

        Assert.Contains(NothingPaidText, SingleBody());
    }

    [Theory]
    [InlineData(PaymentTxStatus.PENDING)]
    [InlineData(PaymentTxStatus.FAILED)]
    public async Task NotifyCancelled_OnlyUnsuccessfulPayments_SaysNothingPaid(PaymentTxStatus status)
    {
        // Link VNPay chưa trả hoặc thẻ lỗi: chưa có tiền về
        await NotifyCancelledAsync(OrderWith(status), refundAmount: 0m);

        Assert.Contains(NothingPaidText, SingleBody());
    }

    [Fact]
    public async Task NotifyCancelled_PaidOrderWithNoRefund_SaysTooLate()
    {
        await NotifyCancelledAsync(OrderWith(PaymentTxStatus.FAILED, PaymentTxStatus.SUCCESS), refundAmount: 0m);

        Assert.Contains(TooLateText, SingleBody());
    }

    [Fact]
    public async Task NotifyCancelled_GuestWalkInWithoutPayment_SendsToGuestEmailAndSaysNothingPaid()
    {
        var booking = OrderWith();
        booking.Customer = null;
        booking.CustomerId = null;
        booking.GuestEmail = "vanglai@test.local";
        booking.GuestName = "Khách tại quầy";

        await NotifyCancelledAsync(booking, refundAmount: 0m);

        var email = Assert.Single(_email.Sent);
        Assert.Equal("vanglai@test.local", email.To);
        Assert.Contains(NothingPaidText, email.Body);
    }

    [Fact]
    public async Task NotifyLatePayment_ShowsTheFullRefundAtTheBranch()
    {
        await Notifications().NotifyLatePaymentAsync(OrderWith(PaymentTxStatus.SUCCESS), 200_000m);

        var body = SingleBody();
        Assert.Contains(RefundAtBranchText, body);
        Assert.Contains(200_000m.ToString("N0") + " VNĐ", body);
    }

    private Task NotifyCancelledAsync(Booking booking, decimal refundAmount) =>
        Notifications().NotifyCancelledAsync(booking, BookingStatus.CONFIRMED, refundAmount);

    private BookingNotificationService Notifications() => new(
        new Mock<IBroadcastService>().Object,
        _email,
        new Mock<ISlotInterestNotificationService>().Object,
        TestConfigurationFactory.Create(),
        TimeProvider.System,
        Mock.Of<Microsoft.Extensions.Options.IOptionsMonitor<SmashCourt_BE.Configurations.BookingPaymentSettings>>(
            m => m.CurrentValue == new SmashCourt_BE.Configurations.BookingPaymentSettings()),
        NullLogger<BookingNotificationService>.Instance);

    private static Booking OrderWith(params PaymentTxStatus[] paymentStatuses)
    {
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.CANCELLED);
        booking.Customer = new User { Id = booking.CustomerId!.Value, Email = "khach@test.local", FullName = "Nguyễn Văn An" };
        booking.Branch = new Branch { Id = booking.BranchId, Name = "SmashCourt Thủ Đức", Address = "1 Võ Văn Ngân", Phone = "0281234567" };
        booking.CancelSource = CancelSourceEnum.CUSTOMER;
        var invoice = new Invoice { Id = Guid.NewGuid(), BookingId = booking.Id, FinalTotal = 200_000m };
        foreach (var status in paymentStatuses)
            invoice.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), InvoiceId = invoice.Id, Amount = 200_000m, Method = PaymentTxMethod.VNPAY, Status = status
            });
        booking.Invoice = invoice;
        return booking;
    }
}
