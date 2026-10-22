using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Tests.Helpers;

/// <summary>BE-UT-09 (phần lịch sử): đếm lịch sử khách theo lý do hủy (spec Phase 6, 4.4.1).</summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-09")]
public class CustomerHistoryCalculatorTests
{
    private static readonly DateOnly PlayDate = new(2026, 10, 10);
    private static readonly TimeOnly PlayEnd = new(20, 0);
    // Thời điểm tạo booking đang xét: sau mọi mốc kết thúc trong các test thường
    private static readonly DateTime RequestedAt = new(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc);

    private static CustomerHistoryEntry Played(BookingStatus status, DateOnly? date = null, TimeOnly? end = null) =>
        new(status, null, null, date ?? PlayDate, end ?? PlayEnd, false, null);

    private static CustomerHistoryEntry Cancelled(
        BookingCancelReason? reason,
        bool paid = false,
        decimal? maxRefundPercent = null,
        DateTime? cancelledAt = null,
        BookingStatus status = BookingStatus.CANCELLED) =>
        new(status, reason, cancelledAt ?? RequestedAt.AddDays(-1), PlayDate, PlayEnd, paid, maxRefundPercent);

    private static CustomerHistorySummary Calculate(params CustomerHistoryEntry[] entries) =>
        CustomerHistoryCalculator.Calculate(entries, RequestedAt);

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    public void Calculate_WhenCancelledAtIsLocalTime_ComparesTheSameInstantAsUtc(int offsetSeconds, int expectedCount)
    {
        // Giá trị Kind = Local (không đến từ Npgsql nhưng có thể do code khác dựng) đổi về UTC theo đúng thời điểm, bất kể múi giờ máy
        var cancelledAt = RequestedAt.ToLocalTime().AddSeconds(offsetSeconds);

        var summary = Calculate(Cancelled(BookingCancelReason.CUSTOMER_CANCELLED, cancelledAt: cancelledAt));

        Assert.Equal(DateTimeKind.Local, cancelledAt.Kind);
        Assert.Equal(expectedCount, summary.CancelledCount);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    public void Calculate_WhenCancelledAtIsUnspecified_IsReadAsUtcNotMachineTime(int offsetSeconds, int expectedCount)
    {
        var cancelledAt = DateTime.SpecifyKind(RequestedAt, DateTimeKind.Unspecified).AddSeconds(offsetSeconds);

        var summary = Calculate(Cancelled(BookingCancelReason.CUSTOMER_CANCELLED, cancelledAt: cancelledAt));

        Assert.Equal(expectedCount, summary.CancelledCount);
    }

    [Fact]
    public void Calculate_WhenNoEntries_ReturnsZerosAndNoLastCompleted()
    {
        var summary = Calculate();

        Assert.Equal(new CustomerHistorySummary(0, 0, 0, 0, 0, null), summary);
    }

    [Fact]
    public void Calculate_WhenCompletedAndNoShow_CountsEachInItsOwnBucket()
    {
        var summary = Calculate(
            Played(BookingStatus.COMPLETED),
            Played(BookingStatus.COMPLETED, PlayDate.AddDays(1)),
            Played(BookingStatus.NO_SHOW, PlayDate.AddDays(2)));

        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(1, summary.NoShowCount);
        Assert.Equal(0, summary.CancelledCount);
    }

    [Fact]
    public void Calculate_WhenSeveralCompleted_LastCompletedIsLatestPlayEndInUtc()
    {
        var summary = Calculate(
            Played(BookingStatus.COMPLETED, new DateOnly(2026, 10, 5), new TimeOnly(21, 0)),
            Played(BookingStatus.COMPLETED, new DateOnly(2026, 10, 12), new TimeOnly(18, 30)),
            Played(BookingStatus.COMPLETED, new DateOnly(2026, 10, 8), new TimeOnly(22, 0)));

        Assert.Equal(new DateTime(2026, 10, 12, 11, 30, 0, DateTimeKind.Utc), summary.LastCompletedAtUtc); // 18:30 VN = 11:30Z
        Assert.Equal(DateTimeKind.Utc, summary.LastCompletedAtUtc!.Value.Kind);
    }

    [Fact]
    public void Calculate_WhenOnlyNoShow_LastCompletedIsNull()
    {
        var summary = Calculate(Played(BookingStatus.NO_SHOW));

        Assert.Null(summary.LastCompletedAtUtc);
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.IN_PROGRESS)]
    public void Calculate_WhenBookingIsStillOpen_IsNotCounted(BookingStatus status)
    {
        var summary = Calculate(Played(status));

        Assert.Equal(new CustomerHistorySummary(0, 0, 0, 0, 0, null), summary);
    }

    [Fact]
    public void Calculate_WhenCompletedBookingHasNoCourt_IsNotCounted()
    {
        var summary = Calculate(new CustomerHistoryEntry(BookingStatus.COMPLETED, null, null, null, null, false, null));

        Assert.Equal(0, summary.CompletedCount);
    }

    // ── Ranh giới "kết thúc trước booking đang xét" ──────────────────────────────

    [Fact]
    public void Calculate_WhenPlayEndsExactlyAtRequestedAt_IsNotCounted()
    {
        var requestedAt = DateTimeHelper.ToUtcFromVietnam(PlayDate, PlayEnd);

        var summary = CustomerHistoryCalculator.Calculate([Played(BookingStatus.COMPLETED)], requestedAt);

        Assert.Equal(0, summary.CompletedCount);
        Assert.Null(summary.LastCompletedAtUtc);
    }

    [Fact]
    public void Calculate_WhenPlayEndedOneSecondBeforeRequestedAt_IsCounted()
    {
        var requestedAt = DateTimeHelper.ToUtcFromVietnam(PlayDate, PlayEnd).AddSeconds(1);

        var summary = CustomerHistoryCalculator.Calculate([Played(BookingStatus.NO_SHOW)], requestedAt);

        Assert.Equal(1, summary.NoShowCount);
    }

    [Fact]
    public void Calculate_WhenPlayEndsAfterRequestedAt_IsNotCounted()
    {
        var summary = CustomerHistoryCalculator.Calculate(
            [Played(BookingStatus.COMPLETED, new DateOnly(2026, 10, 25))], RequestedAt);

        Assert.Equal(0, summary.CompletedCount);
    }

    [Fact]
    public void Calculate_WhenPlayEndedBeforeVietnamMidnightAndRequestedJustAfter_IsCounted()
    {
        // Đơn tạo 00:05 VN ngày 11/10 (17:05Z ngày 10/10): đơn chơi xong 23:59 VN ngày 10/10 thì tính, 00:15 VN ngày 11/10 thì chưa
        var requestedAt = DateTimeHelper.ToUtcFromVietnam(new DateOnly(2026, 10, 11), new TimeOnly(0, 5));

        var summary = CustomerHistoryCalculator.Calculate(
            [
                Played(BookingStatus.COMPLETED, new DateOnly(2026, 10, 10), new TimeOnly(23, 59)),
                Played(BookingStatus.COMPLETED, new DateOnly(2026, 10, 11), new TimeOnly(0, 15))
            ],
            requestedAt);

        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(new DateTime(2026, 10, 10, 16, 59, 0, DateTimeKind.Utc), summary.LastCompletedAtUtc);
    }

    [Fact]
    public void Calculate_WhenCancelledAtEqualsRequestedAt_IsNotCounted()
    {
        var summary = Calculate(Cancelled(BookingCancelReason.PAYMENT_EXPIRED, cancelledAt: RequestedAt));

        Assert.Equal(0, summary.PaymentExpiredCount);
    }

    [Fact]
    public void Calculate_WhenCancelledJustBeforeRequestedAt_IsCounted()
    {
        var summary = Calculate(Cancelled(BookingCancelReason.PAYMENT_EXPIRED, cancelledAt: RequestedAt.AddTicks(-10)));

        Assert.Equal(1, summary.PaymentExpiredCount);
    }

    [Fact]
    public void Calculate_WhenCancelledAfterRequestedAt_IsNotCounted()
    {
        var summary = Calculate(Cancelled(BookingCancelReason.CUSTOMER_CANCELLED, cancelledAt: RequestedAt.AddMinutes(1)));

        Assert.Equal(0, summary.CancelledCount + summary.LateCancelledCount);
    }

    [Fact]
    public void Calculate_WhenCancelledWithoutCancelledAt_IsNotCounted()
    {
        var entry = Cancelled(BookingCancelReason.PAYMENT_EXPIRED) with { CancelledAt = null };

        Assert.Equal(0, Calculate(entry).PaymentExpiredCount);
    }

    // ── Lý do hủy ────────────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_WhenPaymentExpired_CountsOnlyPaymentExpired()
    {
        var summary = Calculate(Cancelled(BookingCancelReason.PAYMENT_EXPIRED));

        Assert.Equal(new CustomerHistorySummary(0, 0, 0, 0, 1, null), summary);
    }

    [Theory]
    [InlineData(BookingCancelReason.APPROVAL_EXPIRED)]
    [InlineData(BookingCancelReason.APPROVAL_REJECTED)]
    [InlineData(BookingCancelReason.POLICY_REJECTED)]
    [InlineData(BookingCancelReason.STAFF_CANCELLED)]
    [InlineData(BookingCancelReason.SYSTEM_CLEANUP)]
    [InlineData(null)]
    public void Calculate_WhenCancelReasonIsNotCustomerFault_IsNotCountedAnywhere(BookingCancelReason? reason)
    {
        var summary = Calculate(Cancelled(reason, paid: true, maxRefundPercent: 0m));

        Assert.Equal(new CustomerHistorySummary(0, 0, 0, 0, 0, null), summary);
    }

    [Fact]
    public void Calculate_WhenCustomerCancelsUnpaidBooking_CountsAsCancelled()
    {
        var summary = Calculate(Cancelled(BookingCancelReason.CUSTOMER_CANCELLED, paid: false));

        Assert.Equal(1, summary.CancelledCount);
        Assert.Equal(0, summary.LateCancelledCount);
    }

    [Theory]
    [InlineData(null, 1)] // đã trả nhưng chưa có bản ghi hoàn = hoàn 0 %
    [InlineData(0.0, 1)]
    [InlineData(50.0, 1)]
    [InlineData(99.99, 1)]
    [InlineData(100.0, 0)]
    public void Calculate_WhenCustomerCancelsPaidBooking_IsLateOnlyBelowFullRefund(double? refundPercent, int expectedLate)
    {
        var percent = refundPercent == null ? (decimal?)null : (decimal)refundPercent.Value;

        var summary = Calculate(Cancelled(BookingCancelReason.CUSTOMER_CANCELLED, paid: true, maxRefundPercent: percent));

        Assert.Equal(expectedLate, summary.LateCancelledCount);
        Assert.Equal(1 - expectedLate, summary.CancelledCount);
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    public void Calculate_WhenAnyCancelledStatus_IsClassifiedByCancelReason(BookingStatus status)
    {
        var summary = Calculate(Cancelled(BookingCancelReason.PAYMENT_EXPIRED, status: status));

        Assert.Equal(1, summary.PaymentExpiredCount);
    }

    [Fact]
    public void Calculate_WhenMixedHistory_CountsEachBucketIndependently()
    {
        var summary = Calculate(
            Played(BookingStatus.COMPLETED),
            Played(BookingStatus.NO_SHOW, PlayDate.AddDays(1)),
            Cancelled(BookingCancelReason.CUSTOMER_CANCELLED),
            Cancelled(BookingCancelReason.CUSTOMER_CANCELLED, paid: true, maxRefundPercent: 50m),
            Cancelled(BookingCancelReason.PAYMENT_EXPIRED),
            Cancelled(BookingCancelReason.PAYMENT_EXPIRED),
            Cancelled(BookingCancelReason.APPROVAL_EXPIRED),
            Cancelled(BookingCancelReason.STAFF_CANCELLED));

        Assert.Equal(1, summary.CompletedCount);
        Assert.Equal(1, summary.NoShowCount);
        Assert.Equal(1, summary.CancelledCount);
        Assert.Equal(1, summary.LateCancelledCount);
        Assert.Equal(2, summary.PaymentExpiredCount);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Calculate_WhenRequestedAtIsNotUtc_Throws(DateTimeKind kind)
    {
        var requestedAt = DateTime.SpecifyKind(RequestedAt, kind);

        Assert.Throws<ArgumentException>(() => CustomerHistoryCalculator.Calculate([], requestedAt));
    }
}
