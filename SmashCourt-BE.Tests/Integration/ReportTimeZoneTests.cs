using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// T-9 trên schema thật (BE-IT-25, phần báo cáo khách mới; plan 6A-6 Task 6): "khách mới trong ngày" tính theo ngày giờ VN,
/// biên là nửa khoảng [00:00 VN ngày đầu; 00:00 VN ngày sau ngày cuối). Công thức cũ lấy ngày UTC nên khách đăng ký 00:00–06:59 VN
/// bị tính sang ngày trước. Mốc dùng năm 2020 để không trùng khách do test khác tạo bằng đồng hồ thật.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-25")]
public sealed class ReportTimeZoneTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Day = new(2020, 11, 20);

    private ReportRepository Repository => new(DbContext);

    // Khách theo giờ VN: 23:59:59 ngày 19 (ngoài), 00:00 ngày 20, 23:59:59.999999 ngày 20 (trong), 00:00 và 06:00 ngày 21 (ngoài)
    private async Task SeedCustomersAroundTheDayAsync()
    {
        await Seeder.SeedCustomerAsync(Utc(2020, 11, 19, 16, 59, 59));
        await Seeder.SeedCustomerAsync(Utc(2020, 11, 19, 17, 0, 0));
        await Seeder.SeedCustomerAsync(Utc(2020, 11, 20, 16, 59, 59).AddTicks(9_999_990));
        await Seeder.SeedCustomerAsync(Utc(2020, 11, 20, 17, 0, 0));
        await Seeder.SeedCustomerAsync(Utc(2020, 11, 20, 23, 0, 0));
    }

    private static DateTime Utc(int y, int m, int d, int h, int min, int s) => new(y, m, d, h, min, s, DateTimeKind.Utc);

    [Fact]
    public async Task DashboardSummary_NewCustomers_CountsByVietnamDay()
    {
        await SeedCustomersAroundTheDayAsync();

        var summary = await Repository.GetDashboardSummaryAsync(Day, Day, branchId: null);

        Assert.Equal(2, summary.NewCustomers);
    }

    [Fact]
    public async Task CustomerStatistics_NewCustomers_CountsByVietnamDay()
    {
        await SeedCustomersAroundTheDayAsync();

        var report = await Repository.GetCustomerStatisticsReportAsync(Day, Day, branchId: null, groupBy: null);

        Assert.Equal(2, report.NewCustomers);
    }

    [Fact]
    public async Task CustomerStatistics_AcquisitionTrend_GroupsByVietnamDay()
    {
        await SeedCustomersAroundTheDayAsync();

        var report = await Repository.GetCustomerStatisticsReportAsync(Day, Day.AddDays(1), branchId: null, groupBy: null);

        Assert.Equal(
            [("2020-11-20", 2), ("2020-11-21", 2)],
            report.AcquisitionTrend.Select(t => (t.Period, t.NewCustomers)).ToArray());
    }

    // Khoảng hợp lệ (≤ 365 ngày) chạm ngày cuối của lịch: biên trên không được tràn (mã cũ trả 200 nên không được thành 500)
    [Fact]
    public async Task LastDayOfTheCalendar_DoesNotOverflow()
    {
        var lastDay = DateOnly.MaxValue;

        var summary = await Repository.GetDashboardSummaryAsync(lastDay.AddDays(-30), lastDay, branchId: null);
        var report = await Repository.GetCustomerStatisticsReportAsync(lastDay.AddDays(-30), lastDay, branchId: null, groupBy: null);

        Assert.Equal(0, summary.NewCustomers);
        Assert.Equal(0, report.NewCustomers);
        Assert.Empty(report.AcquisitionTrend);
    }

    [Fact]
    public async Task CustomerStatistics_RangeOfSeveralDays_IncludesTheWholeLastVietnamDay()
    {
        await SeedCustomersAroundTheDayAsync();

        // 19/11–20/11 VN: thêm khách 23:59:59 ngày 19; vẫn không gồm khách từ 00:00 ngày 21
        var report = await Repository.GetCustomerStatisticsReportAsync(Day.AddDays(-1), Day, branchId: null, groupBy: null);

        Assert.Equal(3, report.NewCustomers);
        Assert.Equal(
            [("2020-11-19", 1), ("2020-11-20", 2)],
            report.AcquisitionTrend.Select(t => (t.Period, t.NewCustomers)).ToArray());
    }
}
