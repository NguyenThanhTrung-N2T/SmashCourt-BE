using SmashCourt_BE.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Helpers;

/// <summary>
/// Múi giờ VN dùng cho mọi phép đổi giờ chơi (A9, hạn link hủy, hạn giữ chỗ). VN không có giờ mùa hè (DST) từ 1975, nên không có ca "nhảy giờ";
/// điều cần khóa là id múi giờ (Windows <c>SE Asia Standard Time</c> / Linux <c>Asia/Ho_Chi_Minh</c>) luôn ra +07:00 cố định, kể cả khi
/// BE chạy trong Docker Linux.
/// </summary>
[TestCategory(TestCategories.Helper)]
public class VietnamTimeZoneTests
{
    [Fact]
    public void VietnamTimeZone_HasNoDaylightSavingTime()
    {
        Assert.False(DateTimeHelper.VNTimezone.SupportsDaylightSavingTime);
        Assert.Equal(TimeSpan.FromHours(7), DateTimeHelper.VNTimezone.BaseUtcOffset);
    }

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    [InlineData(2028)] // năm nhuận
    public void EveryDayOfTheYear_StartsAt17hUtcOfThePreviousDay(int year)
    {
        for (var date = new DateOnly(year, 1, 1); date.Year == year; date = date.AddDays(1))
        {
            var utc = DateTimeHelper.ToUtcFromVietnam(date, TimeOnly.MinValue);

            Assert.Equal(DateTimeKind.Utc, utc.Kind);
            Assert.Equal(date.AddDays(-1).ToDateTime(new TimeOnly(17, 0), DateTimeKind.Utc), utc);
            Assert.Equal(TimeSpan.FromHours(7), DateTimeHelper.VNTimezone.GetUtcOffset(utc));
        }
    }

    [Theory]
    [InlineData("2026-03-29", "02:30")] // ngày đổi giờ mùa hè của châu Âu: VN không đổi
    [InlineData("2026-03-08", "02:30")] // ngày đổi giờ mùa hè của Mỹ
    [InlineData("2026-10-25", "02:30")]
    [InlineData("2026-12-31", "23:59")]
    [InlineData("2027-01-01", "00:00")]
    public void WallClockTimesNeverFallIntoAGapOrRepeat(string date, string time)
    {
        var wall = DateOnly.Parse(date).ToDateTime(TimeOnly.Parse(time));

        Assert.False(DateTimeHelper.VNTimezone.IsInvalidTime(wall));
        Assert.False(DateTimeHelper.VNTimezone.IsAmbiguousTime(wall));
        Assert.Equal(wall.AddHours(-7), DateTimeHelper.ToUtcFromVietnam(DateOnly.Parse(date), TimeOnly.Parse(time)));
    }
}
