using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.DTOs.Report;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.AccessControl;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// T-16 (plan 6A-6 Task 8b): % thay đổi của dashboard Owner và dashboard phân tích của Manager so với kỳ trước <b>cùng số ngày, liền kề</b>
/// khoảng đã chuẩn hóa. Mã cũ tính số ngày từ <c>ToDate 23:59:59.9999999</c> nên kỳ trước dài hơn một ngày, và đọc thẳng
/// <c>filter.FromDate!.Value</c>/<c>ToDate!.Value</c> nên thiếu một trong hai ngày là 500. Kiểm qua khoảng mà service truyền xuống repository.
/// </summary>
public class ReportServiceComparisonPeriodTests
{
    private static readonly Guid BranchId = Guid.NewGuid();

    private readonly Mock<IReportRepository> _repo = new();
    private readonly List<(DateOnly From, DateOnly To, bool IsAllTime)> _summaryCalls = [];

    public ReportServiceComparisonPeriodTests()
    {
        _repo.Setup(r => r.GetDashboardSummaryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .Callback<DateOnly, DateOnly, Guid?, bool>((from, to, _, allTime) => _summaryCalls.Add((from, to, allTime)))
            .ReturnsAsync(() => new DashboardSummaryDto());
    }

    private ReportService Service() => new(
        _repo.Object, Mock.Of<IUserBranchRepository>(), Mock.Of<IBranchScopeResolver>(),
        new MemoryCache(new MemoryCacheOptions()), Mock.Of<ILogger<ReportService>>());

    private static DateOnly D(string value) => DateOnly.Parse(value);

    // Dashboard Owner và dashboard phân tích Manager (ngữ cảnh hệ thống: Guid.Empty + BranchId) dùng cùng luật
    private async Task<DashboardSummaryDto> RunAsync(bool owner, DateOnly? from, DateOnly? to) =>
        owner
            ? (await Service().GetOwnerDashboardAsync(new ReportFilterDto { FromDate = from, ToDate = to }, Guid.NewGuid())).Summary
            : (await Service().GetManagerDashboardAsync(new ReportFilterDto { FromDate = from, ToDate = to, BranchId = BranchId }, Guid.Empty)).Summary;

    private (DateOnly From, DateOnly To) Current => (_summaryCalls[0].From, _summaryCalls[0].To);

    private (DateOnly From, DateOnly To) Previous
    {
        get
        {
            Assert.Equal(2, _summaryCalls.Count);
            Assert.False(_summaryCalls[1].IsAllTime);
            return (_summaryCalls[1].From, _summaryCalls[1].To);
        }
    }

    [Theory]
    [InlineData(true, "2026-11-20", "2026-11-20", "2026-11-19", "2026-11-19")] // 1 ngày
    [InlineData(true, "2026-11-14", "2026-11-20", "2026-11-07", "2026-11-13")] // 7 ngày
    [InlineData(true, "2026-10-22", "2026-11-20", "2026-09-22", "2026-10-21")] // 30 ngày
    [InlineData(true, "2026-03-01", "2026-03-31", "2026-01-29", "2026-02-28")] // cả tháng 3 ⇒ 31 ngày trước đó
    [InlineData(true, "2028-03-01", "2028-03-01", "2028-02-29", "2028-02-29")] // năm nhuận
    [InlineData(true, "2026-01-01", "2026-01-10", "2025-12-22", "2025-12-31")] // qua năm
    [InlineData(true, "2025-11-20", "2026-11-20", "2024-11-19", "2025-11-19")] // tối đa: chênh 365 ngày = 366 ngày
    [InlineData(false, "2026-11-20", "2026-11-20", "2026-11-19", "2026-11-19")]
    [InlineData(false, "2026-11-14", "2026-11-20", "2026-11-07", "2026-11-13")]
    [InlineData(false, "2026-03-01", "2026-03-31", "2026-01-29", "2026-02-28")]
    public async Task PreviousPeriod_HasTheSameLengthAndEndsTheDayBefore(
        bool owner, string from, string to, string expectedFrom, string expectedTo)
    {
        await RunAsync(owner, D(from), D(to));

        Assert.Equal((D(from), D(to)), Current);
        Assert.Equal((D(expectedFrom), D(expectedTo)), Previous);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyToDate_ComparesWithThePeriodBeforeTheNormalizedRange(bool owner)
    {
        // ToDate mà không FromDate ⇒ khoảng chuẩn hóa [ToDate − 30; ToDate] (31 ngày)
        await RunAsync(owner, null, D("2026-11-20"));

        Assert.Equal((D("2026-10-21"), D("2026-11-20")), Current);
        Assert.Equal((D("2026-09-20"), D("2026-10-20")), Previous);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyFromDate_ComparesWithThePeriodBeforeTheNormalizedRange(bool owner)
    {
        // FromDate mà không ToDate ⇒ ToDate = hôm nay theo giờ VN (đồng hồ thật): kiểm quan hệ, không kiểm ngày cụ thể
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);

        await RunAsync(owner, from, null);

        var length = Current.To.DayNumber - Current.From.DayNumber + 1;
        Assert.Equal(from, Current.From);
        Assert.Equal(from.AddDays(-1), Previous.To);
        Assert.Equal(length, Previous.To.DayNumber - Previous.From.DayNumber + 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AllTime_HasNoComparison(bool owner)
    {
        var summary = await RunAsync(owner, null, null);

        Assert.Single(_summaryCalls);
        Assert.True(_summaryCalls[0].IsAllTime);
        Assert.Null(summary.RevenueChangePercent);
    }

    [Theory]
    [InlineData(true, "0001-01-01", "0001-01-31")]
    [InlineData(true, "0001-01-01", "0001-01-01")]
    [InlineData(false, "0001-01-01", "0001-01-31")]
    public async Task NoDaysBeforeTheRange_NoComparisonInsteadOfAnError(bool owner, string from, string to)
    {
        var summary = await RunAsync(owner, D(from), D(to));

        Assert.Single(_summaryCalls);
        Assert.Null(summary.RevenueChangePercent);
        Assert.Null(summary.BookingChangePercent);
        Assert.Null(summary.OccupancyChangePercent);
        Assert.Null(summary.NewCustomerChangePercent);
    }

    [Fact]
    public async Task OneDayAfterTheFirstDayOfTheCalendar_ComparesWithThatDay()
    {
        await RunAsync(true, D("0001-01-02"), D("0001-01-02"));

        Assert.Equal((D("0001-01-01"), D("0001-01-01")), Previous);
    }

    [Fact]
    public async Task ChangePercent_IsComputedAgainstThePreviousPeriodFigures()
    {
        _repo.Setup(r => r.GetDashboardSummaryAsync(D("2026-11-20"), D("2026-11-20"), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .Callback<DateOnly, DateOnly, Guid?, bool>((from, to, _, allTime) => _summaryCalls.Add((from, to, allTime)))
            .ReturnsAsync(new DashboardSummaryDto { TotalRevenue = 300_000m, TotalBookings = 3, NewCustomers = 1 });
        _repo.Setup(r => r.GetDashboardSummaryAsync(D("2026-11-19"), D("2026-11-19"), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .Callback<DateOnly, DateOnly, Guid?, bool>((from, to, _, allTime) => _summaryCalls.Add((from, to, allTime)))
            .ReturnsAsync(new DashboardSummaryDto { TotalRevenue = 200_000m, TotalBookings = 4, NewCustomers = 1 });

        var summary = await RunAsync(true, D("2026-11-20"), D("2026-11-20"));

        Assert.Equal(50m, summary.RevenueChangePercent);
        Assert.Equal(-25m, summary.BookingChangePercent);
        Assert.Equal(0m, summary.NewCustomerChangePercent);
    }
}
