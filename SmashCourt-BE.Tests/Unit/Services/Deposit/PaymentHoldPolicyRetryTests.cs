using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Tests.Unit.Services.Deposit;

/// <summary>
/// BE-UT-08, dòng "thanh toán lại" của spec 7.5 (P6-02, kéo về 6A-3 theo chốt 24/10): hạn mới = min(hạn giữ chỗ, max(hạn hiện tại,
/// now + 10')), không bao giờ rút ngắn, không vượt hạn giữ chỗ; tới hạn giữ chỗ thì báo false để caller trả PAYMENT_HOLD_EXPIRED.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Helper)]
[Trait("Catalog", "BE-UT-08")]
public class PaymentHoldPolicyRetryTests
{
    private static readonly DateTime Created = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc); // 09:00 giờ VN
    private static readonly DateTime Deadline = Created.AddMinutes(15);

    private static (bool Ok, PaymentHoldWindow Window) Retry(DateTime now, DateTime expiresAt, DateTime deadline, int minutes = 10)
    {
        var ok = PaymentHoldPolicy.TryForRetry(now, expiresAt, deadline, minutes, out var window);
        return (ok, window);
    }

    [Fact]
    public void EarlyRetry_ExtendsToNowPlusTen()
    {
        // Đặt 09:00 (hạn 09:10), thanh toán lại 09:03 ⇒ hạn 09:13
        var (ok, window) = Retry(Created.AddMinutes(3), Created.AddMinutes(10), Deadline);

        Assert.True(ok);
        Assert.Equal(Created.AddMinutes(13), window.ExpiresAt);
        Assert.Equal(Deadline, window.HoldDeadline);
    }

    [Fact]
    public void LateRetry_StopsAtTheHoldDeadline()
    {
        // Thanh toán lại 09:08 ⇒ 09:18 vượt hạn giữ chỗ ⇒ 09:15
        var (ok, window) = Retry(Created.AddMinutes(8), Created.AddMinutes(10), Deadline);

        Assert.True(ok);
        Assert.Equal(Deadline, window.ExpiresAt);
    }

    [Fact]
    public void RetryNeverShortensTheCurrentDeadline()
    {
        // Hạn hiện tại 09:14 dài hơn now + 10' (09:12) ⇒ giữ 09:14
        var (ok, window) = Retry(Created.AddMinutes(2), Created.AddMinutes(14), Deadline);

        Assert.True(ok);
        Assert.Equal(Created.AddMinutes(14), window.ExpiresAt);
    }

    [Fact]
    public void ApprovedBooking_RetryAfterTwoMinutes_KeepsTheApprovalDeadline()
    {
        // Spec 9.4: duyệt 09:00 (hạn = hạn giữ chỗ = 09:15), retry 09:02 ⇒ hạn vẫn 09:15
        var (ok, window) = Retry(Created.AddMinutes(2), Deadline, Deadline);

        Assert.True(ok);
        Assert.Equal(Deadline, window.ExpiresAt);
    }

    [Fact]
    public void RepeatedRetries_NeverPassTheHoldDeadline()
    {
        var expires = Created.AddMinutes(10);
        for (var minute = 1; minute < 15; minute++)
        {
            var (ok, window) = Retry(Created.AddMinutes(minute), expires, Deadline);
            Assert.True(ok);
            Assert.True(window.ExpiresAt <= Deadline);
            Assert.True(window.ExpiresAt >= expires);
            expires = window.ExpiresAt;
        }

        Assert.False(Retry(Deadline, expires, Deadline).Ok);
    }

    [Fact]
    public void OneSecondBeforeTheHoldDeadline_StillOk()
    {
        var (ok, window) = Retry(Deadline.AddSeconds(-1), Deadline, Deadline);

        Assert.True(ok);
        Assert.Equal(Deadline, window.ExpiresAt);
    }

    [Fact]
    public void AtTheHoldDeadline_IsOver()
    {
        var (ok, window) = Retry(Deadline, Deadline, Deadline);

        Assert.False(ok);
        Assert.Equal(default, window);
    }

    [Fact]
    public void AfterTheHoldDeadline_IsOver()
    {
        Assert.False(Retry(Deadline.AddMinutes(1), Deadline, Deadline).Ok);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(30)]
    public void UsesConfiguredExtension(int minutes)
    {
        var (_, window) = Retry(Created.AddMinutes(1), Created.AddMinutes(2), Created.AddHours(2), minutes);

        Assert.Equal(Created.AddMinutes(1 + minutes), window.ExpiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NonUtcInput_Throws(int which)
    {
        DateTime Local(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        var now = which == 0 ? Local(Created) : Created;
        var expires = which == 1 ? Local(Created.AddMinutes(10)) : Created.AddMinutes(10);
        var deadline = which == 2 ? Local(Deadline) : Deadline;

        Assert.Throws<ArgumentException>(() => PaymentHoldPolicy.TryForRetry(now, expires, deadline, 10, out _));
    }

    [Fact]
    public void ExtensionBelowOneMinute_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentHoldPolicy.TryForRetry(Created, Created.AddMinutes(10), Deadline, 0, out _));
    }
}
