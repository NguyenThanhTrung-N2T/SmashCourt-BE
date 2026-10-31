using System.Net;
using System.Text.Json;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// T-11 qua HTTP (BE-IT-25; plan 6A-6 Task 8): <c>GET /api/customers/{id}/loyalty-transactions?fromDate=…&amp;toDate=…</c> (OWNER) đi đúng
/// đường model binding thật của query string. Trước khi sửa trả 500 (Npgsql từ chối <c>DateTime Kind = Unspecified</c> với timestamptz).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Catalog", "BE-IT-25")]
public sealed class LoyaltyTransactionFilterHttpTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : HttpTestBase(database, host)
{
    private static DateTime Utc(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private async Task<Guid> SeedCustomerWithTransactionsAsync()
    {
        var customer = await SeedUserAsync(UserRole.CUSTOMER);
        await WithDbAsync(async db =>
        {
            foreach (var (note, createdAt) in new[]
                     {
                         ("19/11 23:50 VN", Utc(2020, 11, 19, 16, 50)),
                         ("20/11 23:30 VN", Utc(2020, 11, 20, 16, 30)),
                         ("21/11 00:10 VN", Utc(2020, 11, 20, 17, 10)),
                     })
            {
                db.LoyaltyTransactions.Add(new LoyaltyTransaction
                {
                    Id = Guid.NewGuid(),
                    UserId = customer.Id,
                    Points = 10,
                    TotalPointsAfter = 10,
                    Type = LoyaltyTransactionType.ADJUST,
                    Note = note,
                    CreatedAt = createdAt
                });
            }
            return await db.SaveChangesAsync();
        });
        return customer.Id;
    }

    private async Task<(HttpStatusCode Status, string[] Notes, string[] CreatedAt)> GetAsync(Guid customerId, string query)
    {
        var owner = await SeedUserAsync(UserRole.OWNER);
        var client = await CreateClientForAsync(owner);
        var response = await client.GetAsync($"/api/customers/{customerId}/loyalty-transactions{query}");
        if (response.StatusCode != HttpStatusCode.OK)
            return (response.StatusCode, [], []);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        return (response.StatusCode,
            items.Select(i => i.GetProperty("note").GetString()!).ToArray(),
            items.Select(i => i.GetProperty("createdAt").GetString()!).ToArray());
    }

    [Fact]
    public async Task FilterByOneDay_Returns200WithTheWholeVietnamDay()
    {
        var customerId = await SeedCustomerWithTransactionsAsync();

        var (status, notes, createdAt) = await GetAsync(customerId, "?fromDate=2020-11-20&toDate=2020-11-20");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["20/11 23:30 VN"], notes);
        Assert.Equal(["20/11/2020 23:30:00"], createdAt); // trả ra theo giờ VN, cùng ngày đã lọc
    }

    [Fact]
    public async Task FilterWithOnlyToDate_IncludesTheLastDay()
    {
        var customerId = await SeedCustomerWithTransactionsAsync();

        var (status, notes, _) = await GetAsync(customerId, "?toDate=2020-11-20");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["20/11 23:30 VN", "19/11 23:50 VN"], notes); // mới nhất trước
    }

    // Biên của kiểu ngày: không được tràn khi đổi sang UTC (00:00 VN ngày 01/01/0001 là trước DateTime.MinValue theo UTC)
    [Theory]
    [InlineData("?fromDate=0001-01-01&toDate=9999-12-31")]
    [InlineData("?fromDate=0001-01-01")]
    [InlineData("?toDate=9999-12-31")]
    public async Task ExtremeDates_Return200WithEverything(string query)
    {
        var customerId = await SeedCustomerWithTransactionsAsync();

        var (status, notes, _) = await GetAsync(customerId, query);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, notes.Length);
    }

    [Fact]
    public async Task WithoutFilter_ReturnsAll()
    {
        var customerId = await SeedCustomerWithTransactionsAsync();

        var (status, notes, _) = await GetAsync(customerId, "");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["21/11 00:10 VN", "20/11 23:30 VN", "19/11 23:50 VN"], notes);
    }
}
