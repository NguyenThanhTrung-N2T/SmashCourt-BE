using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.Helpers;

namespace SmashCourt_BE.Tests.Helpers;

[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Validation)]
[Trait("Category", TestCategories.Helper)]
public class BookingStatusTransitionTests
{
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
}
