using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Infrastructure.HealthChecks;

namespace SmashCourt_BE.Extensions;

/// <summary>
/// Endpoint health cho Docker healthcheck và CD (Epic 6C, gói 6C-1 K5, OPS-04). Cả ba không cần đăng nhập, không lộ URL hay cấu hình:
/// <c>/health/live</c> (tiến trình sống), <c>/health/ready</c> (PostgreSQL), <c>/health/dependencies</c> (TADSS và AI gọi tới được không; luôn 200, không ảnh hưởng ready
/// vì BE vẫn phục vụ khi TADSS lỗi — cọc 100 %).
/// </summary>
public static class HealthCheckExtensions
{
    private const string ReadyTag = "ready";
    private const string DependencyClient = "health-dependencies";
    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(3);

    public static IServiceCollection AddSmashCourtHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag]);
        services.AddHttpClient(DependencyClient, client => client.Timeout = DependencyTimeout)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }

    public static IEndpointRouteBuilder MapSmashCourtHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadyTag) }).AllowAnonymous();
        endpoints.MapGet("/health/dependencies", async (
                IHttpClientFactory httpClientFactory,
                IOptionsMonitor<TadssSettings> tadss,
                IConfiguration configuration,
                CancellationToken cancellationToken) =>
            {
                var client = httpClientFactory.CreateClient(DependencyClient);
                var tadssStatus = ProbeAsync(client, tadss.CurrentValue.BaseUrl, "health/live", cancellationToken);
                var aiStatus = ProbeAsync(client, configuration["AIService:BaseUrl"], "health", cancellationToken);
                return Results.Ok(new DependencyStatus(await tadssStatus, await aiStatus));
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
        return endpoints;
    }

    // "up" khi trả 2xx trong thời hạn; "down" khi lỗi, quá hạn hoặc không phải 2xx; "not_configured" khi chưa đặt địa chỉ
    private static async Task<string> ProbeAsync(HttpClient client, string? baseUrl, string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return "not_configured";
        try
        {
            using var response = await client.GetAsync(new Uri(baseUri, path), cancellationToken);
            return response.IsSuccessStatusCode ? "up" : "down";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "down";
        }
    }

    private sealed record DependencyStatus(string Tadss, string Ai);
}
