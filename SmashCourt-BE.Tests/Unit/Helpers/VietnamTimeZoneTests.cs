using SmashCourt_BE.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Helpers;

/// <summary>
/// Múi giờ VN dùng cho mọi phép đổi giờ chơi (A9, hạn link hủy, hạn giữ chỗ). VN không có giờ mùa hè (DST) từ 1975, nên không có ca "nhảy giờ";
/// điều cần khóa là id múi giờ (Windows <c>SE Asia Standard Time</c> / Linux <c>Asia/Ho_Chi_Minh</c>) luôn ra +07:00 cố định, kể cả khi
/// BE chạy trong Docker Linux.
/// </summary>
[TestCategory(TestCategories.Helper)]
[Trait("Catalog", "BE-UT-12")]
public class VietnamTimeZoneTests
{
    // Không dùng SupportsDaylightSavingTime: trên Linux (dữ liệu IANA) nó là true vì có các lần đổi giờ chuẩn trước 1975 (CI đỏ 31/10),
    // dù không rule nào là giờ mùa hè. Điều cần khóa: không rule nào có DaylightDelta, và không rule nào còn hiệu lực sau 1975.
    [Fact]
    public void VietnamTimeZone_HasNoDaylightSavingTime()
    {
        var rules = DateTimeHelper.VNTimezone.GetAdjustmentRules();

        Assert.All(rules, rule => Assert.Equal(TimeSpan.Zero, rule.DaylightDelta));
        Assert.All(rules, rule => Assert.True(rule.DateEnd.Year <= 1975, $"Rule {rule.DateStart:yyyy-MM-dd} → {rule.DateEnd:yyyy-MM-dd} còn hiệu lực sau 1975"));
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

    // ── Biên ngày VN để lọc cột timestamptz (T-9, T-11): [StartOfVietnamDayUtc(from); EndOfVietnamDayUtcExclusive(to)) ──

    [Theory]
    [InlineData("2026-11-20", "2026-11-19T17:00:00", "2026-11-20T17:00:00")]
    [InlineData("2026-12-31", "2026-12-30T17:00:00", "2026-12-31T17:00:00")] // qua năm
    [InlineData("2028-02-29", "2028-02-28T17:00:00", "2028-02-29T17:00:00")] // năm nhuận
    public void VietnamDayBounds_AreTheTwoMidnightsInUtc(string date, string startUtc, string endUtc)
    {
        var day = DateOnly.Parse(date);

        var start = DateTimeHelper.StartOfVietnamDayUtc(day);
        var end = DateTimeHelper.EndOfVietnamDayUtcExclusive(day);

        Assert.Equal(DateTime.SpecifyKind(DateTime.Parse(startUtc), DateTimeKind.Utc), start);
        Assert.Equal(DateTime.SpecifyKind(DateTime.Parse(endUtc), DateTimeKind.Utc), end);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(DateTimeKind.Utc, end.Kind);
        Assert.Equal(TimeSpan.FromDays(1), end - start);
        Assert.Equal(DateTimeHelper.StartOfVietnamDayUtc(day.AddDays(1)), end); // liền nhau, không hở không chồng
    }

    [Fact]
    public void EndOfVietnamDay_LastDayOfTheCalendar_DoesNotOverflow()
    {
        var end = DateTimeHelper.EndOfVietnamDayUtcExclusive(DateOnly.MaxValue);

        Assert.Equal(DateTime.MaxValue, end);
        Assert.Equal(DateTimeKind.Utc, end.Kind);
    }

    [Fact]
    public void StartOfVietnamDay_FirstDayOfTheCalendar_DoesNotThrow()
    {
        // 00:00 VN ngày 01/01/0001 là trước DateTime.MinValue theo UTC: TimeZoneInfo kẹp về MinValue
        var start = DateTimeHelper.StartOfVietnamDayUtc(DateOnly.MinValue);

        Assert.Equal(DateTime.MinValue, start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }
}
