using System.Net;
using System.Text.Json;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// OPS-04 (Epic 6C, gói 6C-1 K5): ba endpoint health cho Docker healthcheck và CD.
/// <c>/health/live</c> chỉ báo tiến trình sống; <c>/health/ready</c> kiểm PostgreSQL; <c>/health/dependencies</c> báo TADSS và AI có gọi tới được không,
/// luôn 200 và không ảnh hưởng ready (BE vẫn phục vụ khi TADSS lỗi: cọc 100 %). Không cần đăng nhập, không lộ URL hay cấu hình.
/// Host test: TADSS là WireMock, AI trỏ vào cổng 9 (kết nối bị từ chối ngay) nên luôn "down".
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Catalog", "OPS-04")]
public sealed class HealthEndpointTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Live_Returns200WithoutToken()
    {
        var client = await CreateAnonymousClientAsync();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_Returns200WhenDatabaseIsReachable()
    {
        var client = await CreateAnonymousClientAsync();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dependencies_TadssUpAiDown_Returns200WithEachStatus()
    {
        var tadss = await GetTadssAsync();
        tadss.ReplyHealthLive(200);
        var client = await CreateAnonymousClientAsync();

        using var response = await client.GetAsync("/health/dependencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("up", body.GetProperty("tadss").GetString());
        Assert.Equal("down", body.GetProperty("ai").GetString());
    }

    [Fact]
    public async Task Dependencies_TadssUnhealthy_ReportsDownButReadyStays200()
    {
        var tadss = await GetTadssAsync();
        tadss.ReplyHealthLive(503);
        var client = await CreateAnonymousClientAsync();

        using var dependencies = await client.GetAsync("/health/dependencies");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, dependencies.StatusCode);
        Assert.Equal("down", (await JsonAsync(dependencies)).GetProperty("tadss").GetString());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task Dependencies_TadssSlowerThanTimeout_ReportsDownWithinBudget()
    {
        var tadss = await GetTadssAsync();
        tadss.ReplyHealthLive(200, delay: TimeSpan.FromSeconds(5));
        var client = await CreateAnonymousClientAsync();
        var started = DateTime.UtcNow;

        using var response = await client.GetAsync("/health/dependencies");

        Assert.Equal("down", (await JsonAsync(response)).GetProperty("tadss").GetString());
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, TimeSpan.FromSeconds(4.5));
    }

    [Fact]
    public async Task Dependencies_BodyHasOnlyStatusesAndNoUrl()
    {
        var tadss = await GetTadssAsync();
        tadss.ReplyHealthLive(200);
        var client = await CreateAnonymousClientAsync();

        using var response = await client.GetAsync("/health/dependencies");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(new[] { "tadss", "ai" }, JsonDocument.Parse(raw).RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("http", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", raw);
    }
}
