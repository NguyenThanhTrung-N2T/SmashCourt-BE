using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// BE-IT-20, SC6-7 (thêm 06/11 sau N-38): hóa đơn PAID phải thu đủ FinalTotal. Tiền thực thu tính như <c>RecalculatePaymentStatusAsync</c>
/// của BE: tổng Amount − RefundedAmount của Payment SUCCESS thuộc chính hóa đơn đó. Thu thừa (khoản thanh toán thêm) không vi phạm.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-20")]
public sealed class BookingInvariantCheckerInvoiceTests(PostgreSqlIntegrationFixture fixture) : InvariantTestBase(fixture)
{
    private const decimal Total = 230_000m;

    [Fact]
    public async Task Sc67_PaidInvoiceCollectedExactlyOrMore_NoViolation()
    {
        var exact = await SeedPaidInvoiceAsync(InvoicePaymentStatus.PAID, (PaymentTxStatus.SUCCESS, 200_000m, 0m), (PaymentTxStatus.SUCCESS, 30_000m, 0m));
        var extra = await SeedPaidInvoiceAsync(InvoicePaymentStatus.PAID, (PaymentTxStatus.SUCCESS, 250_000m, 0m));

        AssertNone(await CheckAsync(exact, extra));
    }

    [Fact]
    public async Task Sc67_PaidInvoiceMissingAServiceCharge_Violation()
    {
        // Đúng dạng hậu quả của N-38: hóa đơn tính thêm 30 000 đ dịch vụ nhưng check-out chỉ thu theo tổng cũ
        var booking = await SeedPaidInvoiceAsync(InvoicePaymentStatus.PAID, (PaymentTxStatus.SUCCESS, 200_000m, 0m));

        AssertOnly(await CheckAsync(booking), InvariantRules.PaidInvoiceIsCollected, booking);
    }

    [Fact]
    public async Task Sc67_RefundedPartOfASuccessPayment_IsNotCollected()
    {
        var booking = await SeedPaidInvoiceAsync(InvoicePaymentStatus.PAID, (PaymentTxStatus.SUCCESS, Total, 1m));

        AssertOnly(await CheckAsync(booking), InvariantRules.PaidInvoiceIsCollected, booking);
    }

    [Theory]
    [InlineData(PaymentTxStatus.PENDING)]
    [InlineData(PaymentTxStatus.FAILED)]
    [InlineData(PaymentTxStatus.REFUNDED)]
    public async Task Sc67_NonSuccessPayments_DoNotCount(PaymentTxStatus paymentStatus)
    {
        var booking = await SeedPaidInvoiceAsync(InvoicePaymentStatus.PAID, (paymentStatus, Total, 0m));

        AssertOnly(await CheckAsync(booking), InvariantRules.PaidInvoiceIsCollected, booking);
    }

    [Theory]
    [InlineData(InvoicePaymentStatus.UNPAID)]
    [InlineData(InvoicePaymentStatus.PARTIALLY_PAID)]
    public async Task Sc67_InvoiceNotYetPaid_MayBeShort(InvoicePaymentStatus status)
    {
        var booking = await SeedPaidInvoiceAsync(status, (PaymentTxStatus.SUCCESS, 100_000m, 0m));

        AssertNone(await CheckAsync(booking));
    }

    // Đơn đã chơi xong (COMPLETED hợp lệ với mọi điều khác), hóa đơn tổng 230 000 đ ở trạng thái cho trước, Payment (trạng thái, số tiền, đã hoàn)
    private async Task<Guid> SeedPaidInvoiceAsync(
        InvoicePaymentStatus invoiceStatus, params (PaymentTxStatus Status, decimal Amount, decimal Refunded)[] payments)
    {
        var order = await SeedOrderAsync(BookingStatus.COMPLETED);
        order.Invoice.ServiceFee = Total - order.Invoice.FinalTotal;
        order.Invoice.FinalTotal = Total;
        order.Invoice.PaymentStatus = invoiceStatus;
        foreach (var (status, amount, refunded) in payments)
        {
            var payment = TestDataSeeder.NewPayment(order.Invoice.Id, status, amount);
            payment.RefundedAmount = refunded;
            DbContext.Payments.Add(payment);
        }
        await DbContext.SaveChangesAsync();
        return order.Booking.Id;
    }
}
