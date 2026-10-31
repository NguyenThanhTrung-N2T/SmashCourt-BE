using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Check-out thu đúng phần còn lại: tổng hiện tại của hóa đơn (gồm dịch vụ thêm sau) trừ số đã thu, không âm; bằng 0 thì không tạo Payment.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "BE-IT-10")]
public class BookingServiceCheckoutTests
{
    private sealed record Checkout(BookingServiceTestBuilder Builder, Booking Booking, Invoice Invoice);

    /// <summary>Đơn đang chơi: tiền sân 200.000 đ + dịch vụ <paramref name="serviceFee"/>, đã thu <paramref name="collected"/>.</summary>
    private static Checkout Arrange(decimal collected, decimal serviceFee = 20_000m, InvoicePaymentStatus status = InvoicePaymentStatus.PARTIALLY_PAID)
    {
        var builder = new BookingServiceTestBuilder();
        var invoice = TestDataFactory.CreateInvoice(finalTotal: 200_000m + serviceFee, paymentStatus: status);
        invoice.CourtFee = 200_000m;
        invoice.ServiceFee = serviceFee;
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Chi nhánh 1" };
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.CustomerId = null;
        booking.Branch = branch;
        booking.Invoice = invoice;
        invoice.BookingId = booking.Id;
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id, CourtId = court.Id, Court = court, Date = booking.BookingDate,
            StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(0, 1), IsActive = true
        });
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(invoice);
        builder.PaymentRepository.Setup(x => x.GetCollectedAmountAsync(invoice.Id)).ReturnsAsync(collected);
        return new Checkout(builder, booking, invoice);
    }

    private static Task RunAsync(Checkout c) =>
        c.Builder.Build().CheckoutAsync(c.Booking.Id, Guid.NewGuid(), nameof(UserRole.OWNER));

    private static void VerifyCollected(Checkout c, decimal? amount)
    {
        if (amount is { } value)
            c.Builder.PaymentRepository.Verify(x => x.CreateAsync(It.Is<Payment>(p =>
                p.InvoiceId == c.Invoice.Id && p.Amount == value && p.Method == PaymentTxMethod.CASH
                && p.Status == PaymentTxStatus.SUCCESS && p.PaidAt != null)), Times.Once);
        else
            c.Builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        c.Builder.InvoiceRepository.Verify(x => x.SetPaymentStatusAsync(c.Invoice.Id, InvoicePaymentStatus.PAID, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task DepositThirtyPercentPlusService_CollectsTheRestOfTheCourtAndTheService()
    {
        // Mã cũ chỉ thu 20.000 đ dịch vụ, bỏ sót 140.000 đ tiền sân chưa cọc
        var c = Arrange(collected: 60_000m);

        await RunAsync(c);

        VerifyCollected(c, 160_000m);
    }

    [Fact]
    public async Task FullyPaidOnlinePlusService_CollectsOnlyTheService()
    {
        var c = Arrange(collected: 200_000m);

        await RunAsync(c);

        VerifyCollected(c, 20_000m);
    }

    [Fact]
    public async Task PayAtCounterNothingPaid_CollectsEverything()
    {
        var c = Arrange(collected: 0m, status: InvoicePaymentStatus.UNPAID);

        await RunAsync(c);

        VerifyCollected(c, 220_000m);
    }

    [Fact]
    public async Task AlreadyPaidInFull_CreatesNoPayment()
    {
        var c = Arrange(collected: 200_000m, serviceFee: 0m, status: InvoicePaymentStatus.PAID);

        await RunAsync(c);

        VerifyCollected(c, null);
    }

    [Fact]
    public async Task PartlyRefundedPayment_OnlyCountsWhatWasKept()
    {
        // GetCollectedAmountAsync đã trừ phần đã hoàn: còn giữ 150.000 đ
        var c = Arrange(collected: 150_000m);

        await RunAsync(c);

        VerifyCollected(c, 70_000m);
    }

    [Fact]
    public async Task PaidMoreThanTheTotal_CollectsNothingAndLogsTheOverpayment()
    {
        // Trả hai link cọc 100 %: thừa 180.000 đ, nhân viên hoàn tiền mặt tại chi nhánh
        var logger = new ListLogger<SmashCourt_BE.Services.BookingService>();
        var c = Arrange(collected: 400_000m);
        c.Builder.Logger = logger;

        await RunAsync(c);

        VerifyCollected(c, null);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("180"));
    }
}
