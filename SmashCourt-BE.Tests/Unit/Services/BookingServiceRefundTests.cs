using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Nhân viên xác nhận đã hoàn tiền mặt tại chi nhánh: mọi khoản hoàn đang chờ của đơn hoàn tất cùng lúc, số đã hoàn cộng dồn vào
/// từng Payment; hai người cùng xác nhận thì chỉ một người thắng.
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingServiceRefundTests
{
    private sealed record PendingRefund(BookingServiceTestBuilder Builder, Booking Booking, List<Refund> Refunds);

    private static PendingRefund Arrange(params (decimal Paid, decimal AlreadyRefunded, decimal Refund, decimal Percent)[] payments)
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Chi nhánh 1", Address = "1 Lê Lợi" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.CANCELLED_PENDING_REFUND);
        booking.CustomerId = null;
        booking.GuestEmail = "khach@example.com";
        booking.GuestName = "Khách";
        booking.Branch = branch;
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, 200_000m, InvoicePaymentStatus.PARTIALLY_PAID);
        var refunds = payments.Select(p =>
        {
            var payment = TestDataFactory.CreatePayment(booking.Invoice.Id, p.Paid, PaymentTxStatus.SUCCESS, PaymentTxMethod.VNPAY);
            payment.RefundedAmount = p.AlreadyRefunded;
            return new Refund
            {
                Id = Guid.NewGuid(), PaymentId = payment.Id, Payment = payment, Amount = p.Refund,
                RefundPercent = p.Percent, Status = RefundStatus.PENDING
            };
        }).ToList();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.RefundRepository.Setup(x => x.GetPendingByBookingIdAsync(booking.Id)).ReturnsAsync(refunds);
        return new PendingRefund(builder, booking, refunds);
    }

    private static Task ConfirmAsync(PendingRefund r, Guid? staffId = null) =>
        r.Builder.Build().ConfirmRefundAsync(r.Booking.Id, staffId ?? Guid.NewGuid(), nameof(UserRole.OWNER));

    [Fact]
    public async Task TwoPendingRefunds_AreCompletedTogether()
    {
        // Hủy đơn đã cọc (hoàn 50 %) rồi tiền của link cũ mới về (hoàn 100 %)
        var r = Arrange((60_000m, 0m, 30_000m, 50m), (60_000m, 0m, 60_000m, 100m));
        var staffId = Guid.NewGuid();

        await ConfirmAsync(r, staffId);

        Assert.All(r.Refunds, refund =>
        {
            Assert.Equal(RefundStatus.COMPLETED, refund.Status);
            Assert.Equal(staffId, refund.ProcessedBy);
            Assert.NotNull(refund.ProcessedAt);
        });
        Assert.Equal(30_000m, r.Refunds[0].Payment.RefundedAmount);
        Assert.Equal(60_000m, r.Refunds[1].Payment.RefundedAmount);
        r.Builder.RefundRepository.Verify(x => x.UpdateAsync(It.IsAny<Refund>()), Times.Exactly(2));
        r.Builder.PaymentRepository.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Exactly(2));
        r.Builder.BookingRepository.Verify(x => x.UpdateWithStatusCheckAsync(
            r.Booking.Id, BookingStatus.CANCELLED_REFUNDED, BookingStatus.CANCELLED_PENDING_REFUND), Times.Once);
        r.Builder.InvoiceRepository.Verify(x => x.SetPaymentStatusAsync(
            r.Booking.Invoice!.Id, InvoicePaymentStatus.REFUNDED, It.IsAny<DateTime>()), Times.Once);
        // Một email với tổng tiền mặt khách nhận tại chi nhánh
        var email = Assert.Single(r.Builder.Email.To("khach@example.com"));
        Assert.Contains(90_000m.ToString("N0"), email.Body);
    }

    [Fact]
    public async Task PaymentAlreadyPartlyRefunded_AddsToTheRefundedAmount()
    {
        // Mã cũ gán đè refunded_amount = số hoàn lần này
        var r = Arrange((100_000m, 40_000m, 60_000m, 100m));

        await ConfirmAsync(r);

        Assert.Equal(100_000m, r.Refunds[0].Payment.RefundedAmount);
    }

    [Fact]
    public async Task TwoStaffConfirmAtOnce_LoserGetsConflictAndWritesNothing()
    {
        var r = Arrange((60_000m, 0m, 60_000m, 100m));
        r.Builder.BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                r.Booking.Id, BookingStatus.CANCELLED_REFUNDED, BookingStatus.CANCELLED_PENDING_REFUND))
            .ReturnsAsync(0);

        var error = await Assert.ThrowsAsync<AppException>(() => ConfirmAsync(r));

        error.ShouldBeAppException(409, ErrorCodes.Conflict);
        r.Builder.RefundRepository.Verify(x => x.UpdateAsync(It.IsAny<Refund>()), Times.Never);
        r.Builder.PaymentRepository.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Never);
        Assert.Empty(r.Builder.Email.Sent);
    }

    [Fact]
    public async Task NoPendingRefund_NotFound()
    {
        var r = Arrange();

        var error = await Assert.ThrowsAsync<AppException>(() => ConfirmAsync(r));

        error.ShouldBeAppException(404, ErrorCodes.NotFound);
    }
}
