using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// T-10 trên schema thật (BE-IT-25, phần dashboard Manager; plan 6A-6 Task 7): cửa sổ "sắp bắt đầu" (now + 30') và "nguy cơ không đến"
/// (giờ bắt đầu + 15') tính trên <see cref="TimeOnly"/> nên quay vòng qua nửa đêm: từ 23:30 đơn sắp bắt đầu biến mất khỏi KPI và thẻ sân,
/// đơn bắt đầu sau 23:45 không bao giờ hiện "nguy cơ không đến". Slot không qua nửa đêm (chk_booking_courts_valid_time) nên đơn muộn
/// nhất là 23:50–23:59. "now" là giờ tường VN như <c>DateTimeHelper.GetVietnamNow()</c>; mỗi test một chi nhánh riêng.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-25")]
public sealed class ManagerDashboardMidnightTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Today = new(2020, 11, 20);

    private ReportRepository Repository => new(DbContext);

    private static DateTime NowAt(string time) => Today.ToDateTime(TimeOnly.Parse(time));

    private static TimeOnly T(string time) => TimeOnly.Parse(time);

    private async Task<(Branch Branch, Court Court)> SeedBookingAsync(
        string start, string end, BookingStatus status = BookingStatus.CONFIRMED, bool checkedIn = false)
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        await SeedBookingOnCourtAsync(seed.Branch, seed.Court, start, end, status, checkedIn);
        return (seed.Branch, seed.Court);
    }

    private async Task SeedBookingOnCourtAsync(
        Branch branch, Court court, string start, string end, BookingStatus status = BookingStatus.CONFIRMED, bool checkedIn = false)
    {
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(branch.Id, customer.Id, status);
        booking.BookingDate = Today;
        if (checkedIn)
            booking.CheckedInAt = DateTimeHelper.ToUtcFromVietnam(Today, T(start));

        DbContext.Bookings.Add(booking);
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(booking.Id, court.Id, Today, T(start), T(end)));
        await DbContext.SaveChangesAsync();
    }

    private async Task<string> CardStatusAsync(Branch branch, Court court, DateTime now)
    {
        var cards = await Repository.GetManagerDashboardLiveCourtsAsync(branch.Id, Today, now);
        return cards.Single(c => c.CourtId == court.Id).AttentionStatus;
    }

    // ───────────────────────── Sắp bắt đầu (now + 30') ─────────────────────────

    // Từ 23:30, now + 30' sang ngày sau: giới hạn là hết ngày hôm nay, không quay về 00:xx
    [Theory]
    [InlineData("23:30:00")]
    [InlineData("23:30:01")]
    [InlineData("23:45:00")]
    [InlineData("23:49:59")]
    public async Task UpcomingCheckIn_LastSlotOfTheDay_StaysUpcomingAfter2330(string now)
    {
        var (branch, court) = await SeedBookingAsync("23:50", "23:59");

        var kpis = await Repository.GetManagerDashboardKpisAsync(branch.Id, Today, NowAt(now));

        Assert.Equal(1, kpis.UpcomingCheckInsCount);
        Assert.Equal("UPCOMING_CHECK_IN", await CardStatusAsync(branch, court, NowAt(now)));
    }

    // Hồi quy: cửa sổ 30' (tính cả biên), chỉ đơn chưa bắt đầu
    [Theory]
    [InlineData("10:00:00", "10:20", true)]
    [InlineData("10:00:00", "10:30", true)]   // đúng 30'
    [InlineData("10:00:00", "10:31", false)]
    [InlineData("10:00:00", "10:00", false)]  // đã tới giờ bắt đầu
    [InlineData("23:20:00", "23:50", true)]   // đúng 30', chưa qua nửa đêm
    [InlineData("23:19:59", "23:50", false)]
    [InlineData("23:15:00", "23:50", false)]
    [InlineData("23:50:00", "23:50", false)]
    [InlineData("00:00:00", "00:30", true)]   // đầu ngày
    [InlineData("00:00:00", "00:31", false)]
    public async Task UpcomingCheckIn_ThirtyMinuteWindow(string now, string start, bool expected)
    {
        var end = T(start).AddMinutes(9).ToString("HH:mm");
        var (branch, court) = await SeedBookingAsync(start, end);

        var kpis = await Repository.GetManagerDashboardKpisAsync(branch.Id, Today, NowAt(now));
        var card = await CardStatusAsync(branch, court, NowAt(now));

        Assert.Equal(expected ? 1 : 0, kpis.UpcomingCheckInsCount);
        Assert.Equal(expected, card == "UPCOMING_CHECK_IN");
    }

    // Hồi quy: điều kiện trạng thái giữ nguyên khi mở rộng giới hạn tới cuối ngày
    [Fact]
    public async Task UpcomingCheckIn_After2330_StillIgnoresCheckedInAndCancelledBookings()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var second = await Seeder.SeedCourtAsync(seed);
        var third = await Seeder.SeedCourtAsync(seed);
        await SeedBookingOnCourtAsync(seed.Branch, seed.Court, "23:50", "23:59", BookingStatus.CONFIRMED, checkedIn: true);
        await SeedBookingOnCourtAsync(seed.Branch, second, "23:50", "23:59", BookingStatus.CANCELLED);
        await SeedBookingOnCourtAsync(seed.Branch, third, "23:50", "23:59", BookingStatus.PAID_ONLINE);

        var kpis = await Repository.GetManagerDashboardKpisAsync(seed.Branch.Id, Today, NowAt("23:45"));

        Assert.Equal(1, kpis.UpcomingCheckInsCount);
    }

    // ───────────────────────── Nguy cơ không đến (giờ bắt đầu + 15') ─────────────────────────

    // Đơn bắt đầu sau 23:45: giờ bắt đầu + 15' sang ngày sau, trước đây không bao giờ hiện thẻ
    [Theory]
    [InlineData("23:50", "23:50:00")]
    [InlineData("23:50", "23:55:00")]
    [InlineData("23:50", "23:59:00")]
    [InlineData("23:45", "23:59:00")]
    public async Task NoShowRisk_LateSlot_IsShownWithinFifteenMinutes(string start, string now)
    {
        var (branch, court) = await SeedBookingAsync(start, "23:59:30");

        Assert.Equal("NO_SHOW_RISK", await CardStatusAsync(branch, court, NowAt(now)));
    }

    // Hồi quy: cửa sổ [giờ bắt đầu; giờ bắt đầu + 15'] tính cả hai biên, kể cả đầu ngày
    [Theory]
    [InlineData("10:00", "10:00:00", true)]
    [InlineData("10:00", "10:15:00", true)]
    [InlineData("10:00", "10:15:01", false)]
    [InlineData("10:00", "09:59:59", false)]
    [InlineData("00:00", "00:15:00", true)]
    [InlineData("00:00", "00:15:01", false)]
    [InlineData("00:05", "00:10:00", true)]
    public async Task NoShowRisk_FifteenMinuteWindow(string start, string now, bool expected)
    {
        var end = T(start).AddMinutes(50).ToString("HH:mm");
        var (branch, court) = await SeedBookingAsync(start, end);

        Assert.Equal(expected, await CardStatusAsync(branch, court, NowAt(now)) == "NO_SHOW_RISK");
    }

    [Fact]
    public async Task NoShowRisk_CheckedInBooking_IsNotAtRisk()
    {
        var (branch, court) = await SeedBookingAsync("23:50", "23:59", BookingStatus.CONFIRMED, checkedIn: true);

        Assert.NotEqual("NO_SHOW_RISK", await CardStatusAsync(branch, court, NowAt("23:55")));
    }
}
