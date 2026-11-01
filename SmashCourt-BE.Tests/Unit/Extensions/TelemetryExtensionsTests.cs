using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Extensions;

/// <summary>
/// OB-BE-1 (Epic 6C, gói 6C-3a): BE gửi metric, trace, log qua OTLP khi <c>Telemetry:Enabled</c>. Tắt (mặc định) thì không đăng ký provider nào,
/// dev và host test không đổi. Metric phải nghe đúng các meter dashboard "Vận hành" dùng, và histogram thời gian request dùng ít bucket để nằm
/// trong gói miễn phí Grafana Cloud.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "OB-BE-1")]
public class TelemetryExtensionsTests
{
    private static WebApplicationBuilder Builder(Dictionary<string, string?> values)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    [Fact]
    public void AddSmashCourtTelemetry_WhenDisabled_RegistersNoProvider()
    {
        var builder = Builder([]);

        builder.AddSmashCourtTelemetry();
        using var provider = builder.Services.BuildServiceProvider();

        Assert.Null(provider.GetService<MeterProvider>());
        Assert.Null(provider.GetService<TracerProvider>());
    }

    [Fact]
    public void AddSmashCourtTelemetry_WhenEnabled_RegistersMeterAndTracerProviders()
    {
        var builder = Builder(new() { ["Telemetry:Enabled"] = "true", ["Telemetry:OtlpEndpoint"] = "http://127.0.0.1:9" });

        builder.AddSmashCourtTelemetry();
        using var provider = builder.Services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<MeterProvider>());
        Assert.NotNull(provider.GetService<TracerProvider>());
    }

    [Fact]
    public void AddSmashCourtTelemetry_WhenEnabledWithInvalidEndpoint_ThrowsNamingTheKey()
    {
        var builder = Builder(new() { ["Telemetry:Enabled"] = "true", ["Telemetry:OtlpEndpoint"] = "alloy" });

        var error = Assert.Throws<OptionsValidationException>(() => builder.AddSmashCourtTelemetry());

        Assert.Contains("Telemetry:OtlpEndpoint", error.Message);
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Hosting", "http.server.request.duration")]
    [InlineData("System.Net.Http", "http.client.request.duration")]
    [InlineData("Npgsql", "db.client.connections.usage")]
    public void AddSmashCourtMeters_ListensToMetersUsedByDashboard(string meterName, string instrumentName)
    {
        var exported = new List<Metric>();
        using (var meterProvider = Sdk.CreateMeterProviderBuilder().AddSmashCourtMeters().AddInMemoryExporter(exported).Build())
        using (var meter = new Meter(meterName))
        {
            meter.CreateCounter<long>(instrumentName).Add(1);
            meterProvider.ForceFlush();
        }

        Assert.Contains(exported, metric => metric.Name == instrumentName);
    }

    [Fact]
    public void AddSmashCourtMeters_RequestDurationUsesReducedBuckets()
    {
        var exported = new List<Metric>();
        using (var meterProvider = Sdk.CreateMeterProviderBuilder().AddSmashCourtMeters().AddInMemoryExporter(exported).Build())
        using (var meter = new Meter("Microsoft.AspNetCore.Hosting"))
        {
            meter.CreateHistogram<double>("http.server.request.duration", "s").Record(0.03);
            meterProvider.ForceFlush();
        }

        // Bộ xuất in-memory nhận một lượt lúc ForceFlush và một lượt lúc dispose provider: lấy lượt đầu
        var metric = exported.First(m => m.Name == "http.server.request.duration");
        var bounds = new List<double>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            foreach (var bucket in point.GetHistogramBuckets())
                bounds.Add(bucket.ExplicitBound);
        }

        Assert.Equal([.. TelemetryExtensions.RequestDurationBoundariesSeconds, double.PositiveInfinity], bounds);
    }
}
