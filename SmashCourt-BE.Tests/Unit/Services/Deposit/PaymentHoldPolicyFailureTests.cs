using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services.Deposit;

/// <summary>
/// Thanh toán thất bại: hạn thanh toán rút về min(hạn hiện tại, now + ân hạn), không bao giờ kéo dài.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Helper)]
[Trait("Catalog", "BE-UT-08")]
public class PaymentHoldPolicyFailureTests
{
    private static readonly DateTime Created = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc); // 09:00 giờ VN

    [Fact]
    public void FailureEarly_ShortensToNowPlusGrace()
    {
        // Hạn 09:10, thẻ lỗi lúc 09:02 ⇒ còn tới 09:07
        var expiresAt = PaymentHoldPolicy.ForPaymentFailure(Created.AddMinutes(2), Created.AddMinutes(10), 5);

        Assert.Equal(Created.AddMinutes(7), expiresAt);
    }

    [Fact]
    public void FailureNearTheDeadline_KeepsTheShorterDeadline()
    {
        // Hạn 09:10, thẻ lỗi lúc 09:07 ⇒ 09:12 dài hơn hạn cũ ⇒ giữ 09:10
        var expiresAt = PaymentHoldPolicy.ForPaymentFailure(Created.AddMinutes(7), Created.AddMinutes(10), 5);

        Assert.Equal(Created.AddMinutes(10), expiresAt);
    }

    [Fact]
    public void FailureExactlyGraceBeforeTheDeadline_KeepsTheDeadline()
    {
        var expiresAt = PaymentHoldPolicy.ForPaymentFailure(Created.AddMinutes(5), Created.AddMinutes(10), 5);

        Assert.Equal(Created.AddMinutes(10), expiresAt);
    }

    [Fact]
    public void FailureOneSecondBeforeGraceBoundary_Shortens()
    {
        var now = Created.AddMinutes(5).AddSeconds(-1);

        var expiresAt = PaymentHoldPolicy.ForPaymentFailure(now, Created.AddMinutes(10), 5);

        Assert.Equal(now.AddMinutes(5), expiresAt);
        Assert.True(expiresAt < Created.AddMinutes(10));
    }

    [Fact]
    public void FailureAfterTheDeadline_KeepsTheOldDeadline()
    {
        // IPN đến muộn khi hạn đã qua: không được mở lại hạn
        var expiresAt = PaymentHoldPolicy.ForPaymentFailure(Created.AddMinutes(12), Created.AddMinutes(10), 5);

        Assert.Equal(Created.AddMinutes(10), expiresAt);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void NonUtcInput_Throws(DateTimeKind kind)
    {
        var notUtc = DateTime.SpecifyKind(Created, kind);

        Assert.Throws<ArgumentException>(() => PaymentHoldPolicy.ForPaymentFailure(notUtc, Created.AddMinutes(10), 5));
        Assert.Throws<ArgumentException>(() => PaymentHoldPolicy.ForPaymentFailure(Created, notUtc, 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GraceBelowOneMinute_Throws(int graceMinutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PaymentHoldPolicy.ForPaymentFailure(Created, Created.AddMinutes(10), graceMinutes));
    }
}
