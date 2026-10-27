using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.Cancellation;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services.Cancellation;

/// <summary>
/// Luật hủy: % hoàn theo giờ VN quy đổi UTC, tiền hoàn trên số đã trả, đã tới giờ bắt đầu, trả lượt khuyến mãi.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Helper)]
[Trait("Catalog", "BE-UT-10")]
public class CancellationPolicyTests
{
    // Giờ chơi 18:00 giờ VN ngày 21/11 = 11:00 UTC
    private static readonly DateOnly PlayDate = new(2026, 11, 21);
    private static readonly TimeOnly PlayTime = new(18, 0);
    private static readonly DateTime PlayStartUtc = new(2026, 11, 21, 11, 0, 0, DateTimeKind.Utc);

    private static readonly CancelPolicy[] Policies =
    [
        // Cố ý không theo thứ tự: hàm phải tự chọn mốc lớn nhất phù hợp
        new() { HoursBefore = 12, RefundPercent = 50 },
        new() { HoursBefore = 24, RefundPercent = 100 },
        new() { HoursBefore = 2, RefundPercent = 20 }
    ];

    private static DateTime HoursBefore(double hours) => PlayStartUtc.AddHours(-hours);

    // ─── % hoàn ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(24 * 60 + 1, 100)]
    [InlineData(24 * 60, 100)]
    [InlineData(24 * 60 - 1, 50)]
    [InlineData(12 * 60, 50)]
    [InlineData(12 * 60 - 1, 20)]
    [InlineData(2 * 60, 20)]
    [InlineData(2 * 60 - 1, 0)]
    [InlineData(1, 0)]
    public void RefundPercent_AtEachPolicyBoundary(int minutesBefore, decimal expected)
    {
        var percent = CancellationPolicy.RefundPercent(Policies, PlayStartUtc, PlayStartUtc.AddMinutes(-minutesBefore));

        Assert.Equal(expected, percent);
    }

    [Fact]
    public void RefundPercent_EighteenHoursBefore_UsesTheTwelveHourTier()
    {
        // Mã cũ trừ giờ tường VN cho UtcNow nên dư 7 giờ: 18 giờ bị tính như 25 giờ và hoàn 100 %
        Assert.Equal(50, CancellationPolicy.RefundPercent(Policies, PlayStartUtc, HoursBefore(18)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(60 * 7)]
    public void RefundPercent_WithoutZeroHourTier_AtOrAfterStart_IsZero(int minutesAfter)
    {
        Assert.Equal(0, CancellationPolicy.RefundPercent(Policies, PlayStartUtc, PlayStartUtc.AddMinutes(minutesAfter)));
    }

    [Fact]
    public void RefundPercent_ZeroHourTier_AppliesUntilTheStartTime()
    {
        // Chính sách cho phép mốc 0 giờ; đúng giờ bắt đầu vẫn áp mốc đó, qua giờ bắt đầu thì không hoàn
        CancelPolicy[] withZeroTier = [.. Policies, new() { HoursBefore = 0, RefundPercent = 10 }];

        Assert.Equal(10, CancellationPolicy.RefundPercent(withZeroTier, PlayStartUtc, PlayStartUtc));
        Assert.Equal(0, CancellationPolicy.RefundPercent(withZeroTier, PlayStartUtc, PlayStartUtc.AddSeconds(1)));
    }

    [Fact]
    public void RefundPercent_NoPolicies_IsZero()
    {
        Assert.Equal(0, CancellationPolicy.RefundPercent([], PlayStartUtc, HoursBefore(48)));
    }

    // ─── Tiền hoàn ──────────────────────────────────────────────────────

    private static Payment Paid(decimal amount, decimal refunded = 0) => new()
    {
        Id = Guid.NewGuid(), Amount = amount, RefundedAmount = refunded, Status = PaymentTxStatus.SUCCESS
    };

    [Fact]
    public void QuoteRefund_DepositOnly_RefundsOnWhatWasPaid()
    {
        // Đơn 200.000 đ cọc 30 %, hủy sớm: hoàn 100 % của 60.000, không phải 100 % tổng đơn
        var deposit = Paid(60_000);

        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(30), [deposit]);

        Assert.Equal(100, quote.Percent);
        Assert.Equal(60_000, quote.Amount);
        var share = Assert.Single(quote.Shares);
        Assert.Equal(deposit.Id, share.PaymentId);
        Assert.Equal(60_000, share.Amount);
    }

    [Fact]
    public void QuoteRefund_FullPayment_MatchesTheOldFormula()
    {
        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(13), [Paid(200_000)]);

        Assert.Equal(100_000, quote.Amount);
    }

    [Fact]
    public void QuoteRefund_TwoPayments_OneSharePerPayment()
    {
        var first = Paid(60_000);
        var second = Paid(60_000);

        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(13), [first, second]);

        Assert.Equal(60_000, quote.Amount);
        Assert.Equal([first.Id, second.Id], quote.Shares.Select(s => s.PaymentId));
        Assert.All(quote.Shares, s => Assert.Equal(30_000, s.Amount));
    }

    [Fact]
    public void QuoteRefund_PartlyRefundedPayment_OnlyCountsTheRest()
    {
        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(13), [Paid(100_000, refunded: 40_000)]);

        Assert.Equal(30_000, quote.Amount);
    }

    [Theory]
    [InlineData(PaymentTxStatus.PENDING)]
    [InlineData(PaymentTxStatus.FAILED)]
    public void QuoteRefund_IgnoresPaymentsThatDidNotSucceed(PaymentTxStatus status)
    {
        var notPaid = new Payment { Id = Guid.NewGuid(), Amount = 60_000, Status = status };

        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(30), [notPaid]);

        Assert.Equal(0, quote.Amount);
        Assert.Empty(quote.Shares);
    }

    [Fact]
    public void QuoteRefund_NoPayment_ReportsThePercentButZeroAmount()
    {
        // Đơn thu tại quầy chưa trả: xem trước vẫn hiện %, số hoàn 0
        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(30), []);

        Assert.Equal(100, quote.Percent);
        Assert.Equal(0, quote.Amount);
        Assert.Empty(quote.Shares);
    }

    [Fact]
    public void QuoteRefund_ZeroPercent_HasNoShares()
    {
        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(1), [Paid(60_000)]);

        Assert.Equal(0, quote.Amount);
        Assert.Empty(quote.Shares);
    }

    [Fact]
    public void QuoteRefund_RoundsEachShareToWholeDong()
    {
        // 33.333 × 50 % = 16.666,5 ⇒ làm tròn như mã cũ (Math.Round, về số chẵn)
        var quote = CancellationPolicy.QuoteRefund(Policies, PlayStartUtc, HoursBefore(13), [Paid(33_333)]);

        Assert.Equal(16_666, quote.Amount);
    }

    // ─── Đã tới giờ bắt đầu ─────────────────────────────────────────────

    [Fact]
    public void HasStarted_AtTheStartTime_IsTrue()
    {
        Assert.True(CancellationPolicy.HasStarted(PlayDate, PlayTime, PlayStartUtc));
    }

    [Fact]
    public void HasStarted_OneMinuteBefore_IsFalse()
    {
        Assert.False(CancellationPolicy.HasStarted(PlayDate, PlayTime, PlayStartUtc.AddMinutes(-1)));
    }

    [Theory]
    // 00:30 giờ VN ngày 21/11 = 17:30 UTC ngày 20/11: so sánh bằng giờ tường sẽ lệch sang ngày khác
    [InlineData(0, 30, 2026, 11, 20, 17, 30)]
    [InlineData(6, 59, 2026, 11, 20, 23, 59)]
    [InlineData(23, 45, 2026, 11, 21, 16, 45)]
    public void HasStarted_UsesVietnamTimeConvertedToUtc(int hour, int minute, int y, int mo, int d, int utcHour, int utcMinute)
    {
        var startUtc = new DateTime(y, mo, d, utcHour, utcMinute, 0, DateTimeKind.Utc);
        var start = new TimeOnly(hour, minute);

        Assert.False(CancellationPolicy.HasStarted(PlayDate, start, startUtc.AddSeconds(-1)));
        Assert.True(CancellationPolicy.HasStarted(PlayDate, start, startUtc));
    }

    // ─── Trả lượt khuyến mãi ───────────────────────────────────────────

    private static Invoice InvoiceWith(PaymentTiming timing, params PaymentTxStatus[] payments) => new()
    {
        PaymentTiming = timing,
        Payments = payments.Select(s => new Payment { Amount = 10_000, Status = s }).ToList()
    };

    [Fact]
    public void ReleasePromotion_OnlineOrderPaid_IsTrue()
    {
        Assert.True(CancellationPolicy.ShouldReleasePromotionUse(InvoiceWith(PaymentTiming.PREPAID, PaymentTxStatus.SUCCESS)));
    }

    [Theory]
    [InlineData]
    [InlineData(PaymentTxStatus.PENDING)]
    [InlineData(PaymentTxStatus.FAILED, PaymentTxStatus.PENDING)]
    public void ReleasePromotion_OnlineOrderNotPaid_IsFalse(params PaymentTxStatus[] payments)
    {
        // Đơn online chỉ được tính lượt khi tiền về, nên hủy lúc chưa trả không được trả lượt
        Assert.False(CancellationPolicy.ShouldReleasePromotionUse(InvoiceWith(PaymentTiming.PREPAID, payments)));
    }

    [Fact]
    public void ReleasePromotion_PayAtCounter_IsTrue()
    {
        // Xác nhận không qua VNPay, duyệt 0 %, tại quầy trả sau: lượt đã được tính lúc xác nhận
        Assert.True(CancellationPolicy.ShouldReleasePromotionUse(InvoiceWith(PaymentTiming.POSTPAID)));
    }

    // ───────────────────────── Lý do hủy của đơn hết hạn thanh toán (6A-5, B1, spec 4.4.1) ─────────────────────────

    [Theory]
    [InlineData(null, 0, BookingCancelReason.SYSTEM_CLEANUP)]                       // BE dừng giữa hai transaction
    [InlineData(null, 1, BookingCancelReason.PAYMENT_EXPIRED)]                      // dữ liệu trước Phase 6: có Payment, chưa có mức cọc
    [InlineData(DepositSource.MANAGER, 0, BookingCancelReason.PAYMENT_EXPIRED)]     // đã duyệt, khách chưa bấm thanh toán
    [InlineData(DepositSource.TADSS, 0, BookingCancelReason.PAYMENT_EXPIRED)]
    [InlineData(DepositSource.FULL_PAYMENT_FALLBACK, 2, BookingCancelReason.PAYMENT_EXPIRED)]
    public void PaymentExpiryReason_IsSystemCleanupOnlyWithoutDepositAndWithoutPayments(
        DepositSource? depositSource, int paymentCount, BookingCancelReason expected)
    {
        var invoice = InvoiceWith(PaymentTiming.PREPAID, Enumerable.Repeat(PaymentTxStatus.FAILED, paymentCount).ToArray());
        invoice.DepositSource = depositSource;

        Assert.Equal(expected, CancellationPolicy.PaymentExpiryReason(invoice));
    }

    [Fact]
    public void PaymentExpiryReason_WithoutInvoice_IsSystemCleanup()
    {
        // Đơn không có hóa đơn chỉ có thể là đơn dở dang của transaction 1
        Assert.Equal(BookingCancelReason.SYSTEM_CLEANUP, CancellationPolicy.PaymentExpiryReason(null));
    }
}
