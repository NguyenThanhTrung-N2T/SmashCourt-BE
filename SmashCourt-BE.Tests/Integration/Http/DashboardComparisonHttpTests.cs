using System.Net;
using System.Text.Json;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// T-16 qua HTTP (plan 6A-6 Task 8b): <c>GET /api/dashboard/owner</c> với khoảng ngày thiếu một đầu hoặc sát ngày đầu của lịch trả 200
/// (trước khi sửa: 500 do <c>filter.FromDate!.Value</c> hoặc tràn <see cref="DateTime"/>). Phép tính kỳ trước kiểm ở unit test.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class DashboardComparisonHttpTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : HttpTestBase(database, host)
{
    private async Task<(HttpStatusCode Status, JsonElement? Summary)> GetOwnerDashboardAsync(string query)
    {
        var owner = await SeedUserAsync(UserRole.OWNER);
        var client = await CreateClientForAsync(owner);
        var response = await client.GetAsync($"/api/dashboard/owner{query}");
        if (response.StatusCode != HttpStatusCode.OK)
            return (response.StatusCode, null);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, json.RootElement.GetProperty("data").GetProperty("summary").Clone());
    }

    [Theory]
    [InlineData("?toDate=2020-11-20")]
    [InlineData("?fromDate=2020-11-14&toDate=2020-11-20")]
    public async Task DateRange_Returns200WithComparison(string query)
    {
        var (status, summary) = await GetOwnerDashboardAsync(query);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEqual(JsonValueKind.Null, summary!.Value.GetProperty("revenueChangePercent").ValueKind);
    }

    [Fact]
    public async Task OnlyFromDate_Returns200WithComparison()
    {
        // ToDate mặc định là hôm nay (đồng hồ thật) nên FromDate phải trong 365 ngày gần đây
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);

        var (status, summary) = await GetOwnerDashboardAsync($"?fromDate={from:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotEqual(JsonValueKind.Null, summary!.Value.GetProperty("revenueChangePercent").ValueKind);
    }

    [Fact]
    public async Task RangeAtTheFirstDayOfTheCalendar_Returns200WithoutComparison()
    {
        var (status, summary) = await GetOwnerDashboardAsync("?fromDate=0001-01-01&toDate=0001-01-31");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Null, summary!.Value.GetProperty("revenueChangePercent").ValueKind);
    }
}
