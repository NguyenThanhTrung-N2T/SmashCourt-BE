using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Tests.Unit.Services.Deposit;

/// <summary>
/// BE-UT-08, dòng "duyệt" của spec 7.5 (Q2): sau khi Quản lý duyệt mức cọc &gt; 0, hạn thanh toán = min(duyệt + 15', giờ bắt đầu chơi)
/// và cũng là hạn giữ chỗ tuyệt đối mới (ghi đè hạn lúc đặt, H-03). Không còn thời gian (giờ chơi đã tới) thì báo false, không trả mốc đã qua.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Helper)]
[Trait("Catalog", "BE-UT-08")]
public class PaymentHoldPolicyApprovalTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc); // 10:00 giờ VN

    private static (bool Ok, PaymentHoldWindow Window) Approve(DateTime now, DateTime playStart, int minutes = 15)
    {
        var ok = PaymentHoldPolicy.TryForApproval(now, playStart, minutes, out var window);
        return (ok, window);
    }

    [Fact]
    public void PlayFarAway_GivesFullWindowAndDeadlineEqualsExpiry()
    {
        // Duyệt 10:00 cho giờ chơi 18:00 ⇒ hạn 10:15
        var (ok, window) = Approve(Now, Now.AddHours(8));

        Assert.True(ok);
        Assert.Equal(Now.AddMinutes(15), window.ExpiresAt);
        Assert.Equal(window.ExpiresAt, window.HoldDeadline);
        Assert.Equal(DateTimeKind.Utc, window.ExpiresAt.Kind);
        Assert.Equal(DateTimeKind.Utc, window.HoldDeadline.Kind);
    }

    [Fact]
    public void PlayStartsWithinWindow_ExpiryIsPlayStart()
    {
        var playStart = Now.AddMinutes(10);

        var (ok, window) = Approve(Now, playStart);

        Assert.True(ok);
        Assert.Equal(playStart, window.ExpiresAt);
        Assert.Equal(playStart, window.HoldDeadline);
    }

    [Fact]
    public void PlayStartsExactlyAtWindowEnd_ExpiryIsThatMoment()
    {
        var (ok, window) = Approve(Now, Now.AddMinutes(15));

        Assert.True(ok);
        Assert.Equal(Now.AddMinutes(15), window.ExpiresAt);
    }

    [Fact]
    public void PlayStartsOneSecondLater_StillOk()
    {
        var (ok, window) = Approve(Now, Now.AddSeconds(1));

        Assert.True(ok);
        Assert.Equal(Now.AddSeconds(1), window.ExpiresAt);
    }

    [Fact]
    public void PlayStartsNow_NoTimeLeft()
    {
        var (ok, window) = Approve(Now, Now);

        Assert.False(ok);
        Assert.Equal(default, window);
    }

    [Fact]
    public void PlayAlreadyStarted_NoTimeLeft()
    {
        var (ok, _) = Approve(Now, Now.AddMinutes(-30));

        Assert.False(ok);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(60)]
    public void UsesConfiguredMinutes(int minutes)
    {
        var (ok, window) = Approve(Now, Now.AddDays(1), minutes);

        Assert.True(ok);
        Assert.Equal(Now.AddMinutes(minutes), window.ExpiresAt);
    }

    [Fact]
    public void ApprovalJustBeforeMidnightVietnam_CrossesIntoNextDay()
    {
        // 23:55 giờ VN ngày 20/11 = 16:55 UTC; giờ chơi 06:00 giờ VN ngày 21/11
        var approvedAt = new DateTime(2026, 11, 20, 16, 55, 0, DateTimeKind.Utc);
        var playStart = DateTimeHelper.ToUtcFromVietnam(new DateOnly(2026, 11, 21), new TimeOnly(6, 0));

        var (ok, window) = Approve(approvedAt, playStart);

        Assert.True(ok);
        Assert.Equal(new DateTime(2026, 11, 20, 17, 10, 0, DateTimeKind.Utc), window.ExpiresAt); // 00:10 giờ VN ngày 21/11
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void NowNotUtc_Throws(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(() =>
            PaymentHoldPolicy.TryForApproval(DateTime.SpecifyKind(Now, kind), Now.AddHours(1), 15, out _));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void PlayStartNotUtc_Throws(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(() =>
            PaymentHoldPolicy.TryForApproval(Now, DateTime.SpecifyKind(Now.AddHours(1), kind), 15, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void MinutesBelowOne_Throws(int minutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentHoldPolicy.TryForApproval(Now, Now.AddHours(1), minutes, out _));
    }
}
