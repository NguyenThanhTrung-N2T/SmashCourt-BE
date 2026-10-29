using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services.Deposit;

/// <summary>
/// BE-UT-08, hạn link thanh toán (P6-27, plan 6A-6 Task 1): link hết hạn trước hạn thanh toán của đơn một khoảng đệm; link phải sống
/// ít nhất <see cref="PaymentHoldPolicy.MinPaymentLinkLifetime"/>, không đủ thì báo false để caller trả PAYMENT_HOLD_EXPIRED.
/// So sánh trên DateTime trong bộ nhớ nên biên kiểm tới từng tick.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Helper)]
[Trait("Catalog", "BE-UT-08")]
public class PaymentHoldPolicyLinkTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc); // 10:00 giờ VN
    private const int Buffer = 2;

    private static (bool Ok, DateTime LinkExpiresAt) Link(DateTime now, DateTime expiresAt, int buffer = Buffer)
    {
        var ok = PaymentHoldPolicy.TryGetPaymentLinkExpiry(now, expiresAt, buffer, out var linkExpiresAt);
        return (ok, linkExpiresAt);
    }

    [Fact]
    public void NewBookingWindow_LinkEndsTwoMinutesBeforeThePaymentDeadline()
    {
        // Đặt 10:00, hạn thanh toán 10:10 ⇒ link hết hạn 10:08
        var (ok, link) = Link(Now, Now.AddMinutes(10));

        Assert.True(ok);
        Assert.Equal(Now.AddMinutes(8), link);
        Assert.Equal(DateTimeKind.Utc, link.Kind);
    }

    [Fact]
    public void MinimumLifetimeIsOneMinute()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), PaymentHoldPolicy.MinPaymentLinkLifetime);
    }

    [Fact]
    public void ExactlyThreeMinutesLeft_LinkLivesExactlyOneMinute()
    {
        var (ok, link) = Link(Now, Now.AddMinutes(3));

        Assert.True(ok);
        Assert.Equal(Now.AddMinutes(1), link);
    }

    [Fact]
    public void ThreeMinutesMinusOneTick_NotEnoughTime()
    {
        var (ok, link) = Link(Now, Now.AddMinutes(3).AddTicks(-1));

        Assert.False(ok);
        Assert.Equal(default, link);
    }

    [Fact]
    public void ExactlyTheBufferLeft_NotEnoughTime()
    {
        // Còn đúng 2 phút ⇒ link sống 0
        Assert.False(Link(Now, Now.AddMinutes(Buffer)).Ok);
    }

    [Fact]
    public void ExpiryIsNow_NotEnoughTime()
    {
        Assert.False(Link(Now, Now).Ok);
    }

    [Fact]
    public void ExpiryAlreadyPassed_NotEnoughTime()
    {
        Assert.False(Link(Now, Now.AddMinutes(-5)).Ok);
    }

    [Fact]
    public void BufferZero_LinkEndsWithThePaymentDeadline()
    {
        var (ok, link) = Link(Now, Now.AddMinutes(1), buffer: 0);

        Assert.True(ok);
        Assert.Equal(Now.AddMinutes(1), link);
        Assert.False(Link(Now, Now.AddMinutes(1).AddTicks(-1), buffer: 0).Ok);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void UsesConfiguredBuffer(int buffer)
    {
        var (ok, link) = Link(Now, Now.AddMinutes(10), buffer);

        Assert.True(ok);
        Assert.Equal(Now.AddMinutes(10 - buffer), link);
    }

    [Fact]
    public void KeepsSubSecondPrecisionOfTheDeadline()
    {
        // Hạn đọc từ DB có phần µs; link bám đúng hạn đó, không làm tròn
        var expiresAt = Now.AddMinutes(10).AddTicks(1_234_560);

        var (ok, link) = Link(Now, expiresAt);

        Assert.True(ok);
        Assert.Equal(expiresAt.AddMinutes(-Buffer), link);
    }

    [Fact]
    public void AcrossVietnamMidnight_IsPlainUtcArithmetic()
    {
        // Đặt 23:55 giờ VN (16:55 UTC), hạn 00:05 ngày sau ⇒ link 00:03 giờ VN (17:03 UTC)
        var now = new DateTime(2026, 11, 20, 16, 55, 0, DateTimeKind.Utc);

        var (ok, link) = Link(now, now.AddMinutes(10));

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 11, 20, 17, 3, 0, DateTimeKind.Utc), link);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void NowNotUtc_Throws(DateTimeKind kind)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PaymentHoldPolicy.TryGetPaymentLinkExpiry(DateTime.SpecifyKind(Now, kind), Now.AddMinutes(10), Buffer, out _));
        Assert.Equal("nowUtc", ex.ParamName);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void DeadlineNotUtc_Throws(DateTimeKind kind)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PaymentHoldPolicy.TryGetPaymentLinkExpiry(Now, DateTime.SpecifyKind(Now.AddMinutes(10), kind), Buffer, out _));
        Assert.Equal("paymentExpiresAtUtc", ex.ParamName);
    }

    [Fact]
    public void NegativeBuffer_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PaymentHoldPolicy.TryGetPaymentLinkExpiry(Now, Now.AddMinutes(10), -1, out _));
    }
}
