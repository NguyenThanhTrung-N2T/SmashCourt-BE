using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Helpers;

[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Validation)]
[Trait("Category", TestCategories.Helper)]
public class BookingStatusTransitionTests
{
    // Bảng chuyển trạng thái đầy đủ (Đồ án 1 + Phase 6): mọi cặp ngoài danh sách này phải bị từ chối
    private static readonly HashSet<(BookingStatus From, BookingStatus To)> AllowedTransitions =
    [
        (BookingStatus.PENDING, BookingStatus.PAID_ONLINE),
        (BookingStatus.PENDING, BookingStatus.CANCELLED),
        (BookingStatus.PENDING, BookingStatus.CONFIRMED),
        (BookingStatus.PENDING_APPROVAL, BookingStatus.PENDING),
        (BookingStatus.PENDING_APPROVAL, BookingStatus.CONFIRMED),
        (BookingStatus.PENDING_APPROVAL, BookingStatus.CANCELLED),
        (BookingStatus.CONFIRMED, BookingStatus.IN_PROGRESS),
        (BookingStatus.CONFIRMED, BookingStatus.CANCELLED),
        (BookingStatus.CONFIRMED, BookingStatus.NO_SHOW),
        (BookingStatus.PAID_ONLINE, BookingStatus.IN_PROGRESS),
        (BookingStatus.PAID_ONLINE, BookingStatus.CANCELLED_PENDING_REFUND),
        (BookingStatus.PAID_ONLINE, BookingStatus.CANCELLED),
        (BookingStatus.PAID_ONLINE, BookingStatus.NO_SHOW),
        (BookingStatus.PAID_ONLINE, BookingStatus.COMPLETED),
        (BookingStatus.IN_PROGRESS, BookingStatus.PENDING_PAYMENT),
        (BookingStatus.IN_PROGRESS, BookingStatus.COMPLETED),
        (BookingStatus.PENDING_PAYMENT, BookingStatus.COMPLETED),
        (BookingStatus.CANCELLED_PENDING_REFUND, BookingStatus.CANCELLED_REFUNDED),
        (BookingStatus.CANCELLED, BookingStatus.CANCELLED_PENDING_REFUND),
        // Tiền của một link khác về sau khi đã hoàn xong: đơn quay lại chờ hoàn cho khoản mới
        (BookingStatus.CANCELLED_REFUNDED, BookingStatus.CANCELLED_PENDING_REFUND)
    ];

    public static TheoryData<BookingStatus, BookingStatus> AllPairs()
    {
        var data = new TheoryData<BookingStatus, BookingStatus>();
        foreach (var from in Enum.GetValues<BookingStatus>())
            foreach (var to in Enum.GetValues<BookingStatus>())
                data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_ForEveryPairOfStatuses_MatchesTheDocumentedTable(BookingStatus from, BookingStatus to)
    {
        // Thêm trạng thái hoặc cạnh mới mà không cập nhật bảng (và spec 4.x) sẽ làm test này đỏ
        Assert.Equal(AllowedTransitions.Contains((from, to)), BookingStatusTransition.CanTransition(from, to));
    }

    [Fact]
    public void CanTransition_FromTerminalStatuses_AllowsNothing()
    {
        foreach (var terminal in new[] { BookingStatus.COMPLETED, BookingStatus.NO_SHOW })
            foreach (var to in Enum.GetValues<BookingStatus>())
                Assert.False(BookingStatusTransition.CanTransition(terminal, to), $"{terminal} -> {to}");
    }

    [Fact]
    public void CanTransition_FromRefunded_OnlyBackToPendingRefund()
    {
        foreach (var to in Enum.GetValues<BookingStatus>())
            Assert.Equal(to == BookingStatus.CANCELLED_PENDING_REFUND,
                BookingStatusTransition.CanTransition(BookingStatus.CANCELLED_REFUNDED, to));
    }

    [Theory]
    [InlineData(BookingStatus.PENDING, BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.PENDING, BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CONFIRMED, BookingStatus.IN_PROGRESS)]
    [InlineData(BookingStatus.PAID_ONLINE, BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND, BookingStatus.CANCELLED_REFUNDED)]
    public void CanTransition_WhenTransitionDefined_ReturnsTrue(BookingStatus from, BookingStatus to)
    {
        var result = BookingStatusTransition.CanTransition(from, to);

        Assert.True(result);
    }

    [Theory]
    [InlineData(BookingStatus.PENDING, BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.COMPLETED, BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED, BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.IN_PROGRESS, BookingStatus.PENDING)]
    public void CanTransition_WhenTransitionUndefined_ReturnsFalse(BookingStatus from, BookingStatus to)
    {
        var result = BookingStatusTransition.CanTransition(from, to);

        Assert.False(result);
    }

    [Fact]
    public void ValidateTransition_WhenInvalidTransition_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BookingStatusTransition.ValidateTransition(BookingStatus.COMPLETED, BookingStatus.PENDING));

        Assert.Contains("COMPLETED -> PENDING", exception.Message);
    }

    [Fact]
    public void ValidateTransition_WhenValidTransition_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            BookingStatusTransition.ValidateTransition(BookingStatus.PENDING, BookingStatus.PAID_ONLINE));

        Assert.Null(exception);
    }

    [Fact]
    public void GetActiveStatuses_WhenCalled_ContainsExpectedStatuses()
    {
        var activeStatuses = BookingStatusTransition.GetActiveStatuses();

        Assert.Contains(BookingStatus.PAID_ONLINE, activeStatuses);
        Assert.Contains(BookingStatus.CONFIRMED, activeStatuses);
        Assert.DoesNotContain(BookingStatus.COMPLETED, activeStatuses);
        Assert.DoesNotContain(BookingStatus.CANCELLED, activeStatuses);
    }

    [Fact]
    public void GetNoShowEligibleStatuses_WhenCalled_ReturnsCorrectStatuses()
    {
        var noShowEligible = BookingStatusTransition.GetNoShowEligibleStatuses();

        Assert.Equal([BookingStatus.CONFIRMED, BookingStatus.PAID_ONLINE], noShowEligible);
    }

    [Fact]
    public void CanTransition_WhenSameStatus_ReturnsFalse()
    {
        var result = BookingStatusTransition.CanTransition(BookingStatus.PENDING, BookingStatus.PENDING);

        Assert.False(result);
    }

    // ── Phase 6: PENDING_APPROVAL (BE-UT-06, BE-UT-11) ──

    [Fact]
    [Trait("Catalog", "BE-UT-06")]
    public void GetActiveStatuses_WhenPendingApproval_HoldsTheCourt()
    {
        Assert.Contains(BookingStatus.PENDING_APPROVAL, BookingStatusTransition.GetActiveStatuses());
        Assert.DoesNotContain(BookingStatus.PENDING_APPROVAL, BookingStatusTransition.GetNonHoldingStatuses());
    }

    [Fact]
    [Trait("Catalog", "BE-UT-06")]
    public void GetNoShowEligibleStatuses_WhenPendingApproval_IsNotEligible()
    {
        Assert.DoesNotContain(BookingStatus.PENDING_APPROVAL, BookingStatusTransition.GetNoShowEligibleStatuses());
    }

    [Theory]
    [Trait("Catalog", "BE-UT-06")]
    [InlineData(BookingStatus.PENDING_APPROVAL, BookingStatus.PENDING)]     // duyệt, cọc > 0
    [InlineData(BookingStatus.PENDING_APPROVAL, BookingStatus.CONFIRMED)]   // duyệt cọc 0%
    [InlineData(BookingStatus.PENDING_APPROVAL, BookingStatus.CANCELLED)]   // từ chối / hết hạn duyệt / hủy
    [InlineData(BookingStatus.PENDING, BookingStatus.CONFIRMED)]            // cọc áp dụng nhỏ hơn mức tối thiểu
    [InlineData(BookingStatus.PAID_ONLINE, BookingStatus.CANCELLED)]        // hủy với hoàn 0%
    [InlineData(BookingStatus.CANCELLED, BookingStatus.CANCELLED_PENDING_REFUND)] // tiền về sau khi hủy
    public void CanTransition_WhenPhase6Edge_ReturnsTrue(BookingStatus from, BookingStatus to)
    {
        Assert.True(BookingStatusTransition.CanTransition(from, to));
    }

    [Fact]
    [Trait("Catalog", "BE-UT-06")]
    public void CanTransition_WhenLeavingPendingApproval_OnlyAllowsDocumentedTargets()
    {
        var allowed = Enum.GetValues<BookingStatus>()
            .Where(to => BookingStatusTransition.CanTransition(BookingStatus.PENDING_APPROVAL, to))
            .ToHashSet();

        Assert.Equal(
            new HashSet<BookingStatus> { BookingStatus.PENDING, BookingStatus.CONFIRMED, BookingStatus.CANCELLED },
            allowed);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-06")]
    public void CanTransition_WhenEnteringPendingApproval_IsNeverAllowedFromAnyStatus()
    {
        // Đơn chỉ vào PENDING_APPROVAL lúc tạo (kết quả NEED_APPROVAL), không chuyển từ trạng thái khác
        var sources = Enum.GetValues<BookingStatus>()
            .Where(from => BookingStatusTransition.CanTransition(from, BookingStatus.PENDING_APPROVAL));

        Assert.Empty(sources);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-11")]
    public void BookingStatus_WhenAnyValue_IsClassifiedAsHoldingOrNonHolding()
    {
        // Thêm giá trị BookingStatus mới mà không phân loại ở BookingStatusTransition ⇒ test này đỏ
        var holding = BookingStatusTransition.GetActiveStatuses().ToHashSet();
        var nonHolding = BookingStatusTransition.GetNonHoldingStatuses().ToHashSet();

        var unclassified = Enum.GetValues<BookingStatus>().Where(s => !holding.Contains(s) && !nonHolding.Contains(s)).ToList();

        Assert.True(unclassified.Count == 0, $"BookingStatus chưa phân loại: {string.Join(", ", unclassified)}");
    }

    [Fact]
    [Trait("Catalog", "BE-UT-11")]
    public void BookingStatus_WhenClassified_NoValueIsBothHoldingAndNonHolding()
    {
        var overlap = BookingStatusTransition.GetActiveStatuses().Intersect(BookingStatusTransition.GetNonHoldingStatuses());

        Assert.Empty(overlap);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-11")]
    public void GetCancelledStatuses_WhenCalled_AreAllNonHolding()
    {
        Assert.Equal(3, BookingStatusTransition.GetCancelledStatuses().Length);
        Assert.All(BookingStatusTransition.GetCancelledStatuses(),
            s => Assert.Contains(s, BookingStatusTransition.GetNonHoldingStatuses()));
    }
}
