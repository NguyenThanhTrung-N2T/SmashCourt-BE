using System.Net;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Check-out trên host thật: thu tại quầy đúng <c>max(0, finalTotal − số đã thu)</c> với mọi loại đơn (trả đủ online, cọc, trả hai lần,
/// tại quầy trả sau / trả ngay), và tích điểm đúng một lần trong transaction lồng của check-out.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class CheckoutFlowTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    // ───────────────────────── Trả hai lần (C1) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task PaidInFullTwice_CheckoutCollectsNothingAndLogsTheOverpayment()
    {
        var order = await BookPendingAsync();
        await PayBothLinksAsync(order.Customer, order.BookingId);
        await StartPlayingAsync(order.BookingId);
        var factory = await GetFactoryAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        Assert.Equal(InvoicePaymentStatus.PAID, booking.Invoice!.PaymentStatus);
        Assert.DoesNotContain(booking.Invoice.Payments, p => p.Method == PaymentTxMethod.CASH);
        Assert.Equal(400_000m, booking.Invoice.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount));
        // Phần thừa 200.000 đ phải hoàn tay tại chi nhánh: log Error là dấu vết duy nhất cho nhân viên
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Error
            && e.Message.Contains(order.BookingId.ToString()) && e.Message.Contains("cần hoàn tại chi nhánh"));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task DepositPaidTwice_CheckoutCollectsOnlyWhatIsStillOwed()
    {
        var pending = await ApproveDepositAsync();
        Assert.Equal(60_000m, await PayBothLinksAsync(pending.Customer, pending.BookingId));
        await StartPlayingAsync(pending.BookingId);
        await AddServiceFeeAsync(pending.BookingId, 20_000m);

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(await SeedStaffAsync(pending.Court.BranchId), pending.BookingId));

        var invoice = (await RequireBookingAsync(pending.BookingId)).Invoice!;
        // 200.000 tiền sân + 20.000 dịch vụ − 2 × 60.000 đã cọc
        Assert.Equal(100_000m, Assert.Single(invoice.Payments, p => p.Method == PaymentTxMethod.CASH).Amount);
        Assert.Equal(invoice.FinalTotal, invoice.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount));
    }

    // ───────────────────────── Đơn tại quầy ─────────────────────────

    [Theory]
    [Trait("Catalog", "BE-IT-10")]
    [InlineData(false, 220_000)] // trả sau: thu tiền sân + dịch vụ
    [InlineData(true, 20_000)]   // đã trả tiền sân lúc tạo: chỉ thu dịch vụ
    public async Task WalkIn_CheckoutCollectsTheRestAtTheCounter(bool payNow, int expectedCash)
    {
        var court = await SeedCourtAsync(200_000m);
        var staff = await SeedStaffAsync(court.BranchId);
        var bookingId = await CreateWalkInAsync(staff, court, payNow);
        await StartPlayingAsync(bookingId);
        await AddServiceFeeAsync(bookingId, 20_000m);
        var before = DateTime.UtcNow;

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(staff, bookingId));

        var after = DateTime.UtcNow;
        var invoice = (await RequireBookingAsync(bookingId)).Invoice!;
        Assert.Equal(InvoicePaymentStatus.PAID, invoice.PaymentStatus);
        // Khoản thu lúc check-out là khoản CASH ghi sau mốc "before" (cùng đồng hồ với BE); khoản trả ngay lúc tạo đơn ghi trước đó
        var collected = Assert.Single(invoice.Payments, p => p.Method == PaymentTxMethod.CASH && p.PaidAt >= before);
        Assert.Equal(expectedCash, collected.Amount);
        Assert.Equal(DateTimeKind.Utc, collected.PaidAt!.Value.Kind);
        Assert.InRange(collected.PaidAt.Value, before, after);
        Assert.Equal(220_000m, invoice.Payments.Where(p => p.Status == PaymentTxStatus.SUCCESS).Sum(p => p.Amount));
    }

    // ───────────────────────── Tích điểm ─────────────────────────

    [Theory]
    [Trait("Catalog", "BE-IT-10")]
    [InlineData(199_999, 199)] // làm tròn xuống, không làm tròn lên 200
    [InlineData(200_000, 200)]
    public async Task Checkout_EarnsOnePointPerThousandOfTheCourtFee_RoundedDown(int courtPrice, int expectedPoints)
    {
        var order = await BookPaidAsync(await SeedCourtAsync(courtPrice));
        await GiveLoyaltyAsync(order.Customer.Id);
        await StartPlayingAsync(order.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));

        Assert.Equal(expectedPoints, await ReadPointsAsync(order.Customer.Id));
        var earn = Assert.Single(await ReadLoyaltyTransactionsAsync(order.BookingId));
        Assert.Equal(LoyaltyTransactionType.EARN, earn.Type);
        Assert.Equal(expectedPoints, earn.Points);
        Assert.Equal(expectedPoints, earn.TotalPointsAfter);
    }

    public enum OrderKind { PaidOnline, DepositPaid, WalkInForAMember }

    [Theory]
    [Trait("Catalog", "BE-IT-10")]
    [InlineData(OrderKind.PaidOnline)]
    [InlineData(OrderKind.DepositPaid)]
    [InlineData(OrderKind.WalkInForAMember)]
    public async Task Checkout_EveryKindOfOrder_EarnsPointsOnTheCourtFee(OrderKind kind)
    {
        // Tích điểm chạy trong scope lồng của check-out và tự nuốt lỗi: chỉ số điểm trong DB chứng minh scope lồng chạy được
        var (bookingId, customerId, branchId) = await OrderOfKindAsync(kind);
        await GiveLoyaltyAsync(customerId);
        await StartPlayingAsync(bookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(await SeedStaffAsync(branchId), bookingId));

        Assert.Equal(200, await ReadPointsAsync(customerId));
        Assert.Single(await ReadLoyaltyTransactionsAsync(bookingId), t => t.Type == LoyaltyTransactionType.EARN && t.Points == 200);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-10")]
    public async Task TwoCheckoutsAtOnce_EarnPointsOnce()
    {
        var order = await BookPaidAsync();
        await GiveLoyaltyAsync(order.Customer.Id);
        await StartPlayingAsync(order.BookingId);
        var staff = await SeedStaffAsync(order.Court.BranchId);

        var (a, b) = await (CheckoutAsync(staff, order.BookingId), CheckoutAsync(staff, order.BookingId)).WhenBoth();

        Assert.DoesNotContain(HttpStatusCode.InternalServerError, new[] { a.Status, b.Status });
        Assert.Single(new[] { a.Status, b.Status }, s => s == HttpStatusCode.OK);
        Assert.Equal(200, await ReadPointsAsync(order.Customer.Id));
        Assert.Single(await ReadLoyaltyTransactionsAsync(order.BookingId));
    }

    private async Task<(Guid BookingId, Guid CustomerId, Guid BranchId)> OrderOfKindAsync(OrderKind kind)
    {
        switch (kind)
        {
            case OrderKind.PaidOnline:
                var paid = await BookPaidAsync();
                return (paid.BookingId, paid.Customer.Id, paid.Court.BranchId);
            case OrderKind.DepositPaid:
                var deposit = await BookDepositPaidAsync();
                return (deposit.BookingId, deposit.Customer.Id, deposit.Court.BranchId);
            default:
                var court = await SeedCourtAsync(200_000m);
                var member = await SeedUserAsync();
                var bookingId = await CreateWalkInAsync(await SeedStaffAsync(court.BranchId), court, payNow: false, member.Id);
                return (bookingId, member.Id, court.BranchId);
        }
    }
}
