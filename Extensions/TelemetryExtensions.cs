using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Infrastructure.Telemetry;

namespace SmashCourt_BE.Extensions;

/// <summary>
/// OpenTelemetry của BE (Epic 6C, gói 6C-3a, OB-BE-1, OB-BE-2): metric (ASP.NET Core, HttpClient, Npgsql, runtime .NET), trace (request vào BE ⇒ DB ⇒
/// TADSS/AI, header <c>traceparent</c> do HttpClient tự gửi) và log, cùng xuất OTLP chạy nền nên collector chết không ảnh hưởng request.
/// Tắt (mặc định) thì không đăng ký gì.
/// </summary>
public static class TelemetryExtensions
{
    // Biên bucket (giây) của thời gian xử lý request: 10 biên thay vì 14 của ASP.NET Core để số chuỗi nằm trong gói miễn phí Grafana Cloud
    public static readonly IReadOnlyList<double> RequestDurationBoundariesSeconds = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private const string RequestDurationInstrument = "http.server.request.duration";
    private const string NpgsqlMeter = "Npgsql";
    private const string HealthPathPrefix = "/health";
    private const string TagUrlQuery = "url.query";

    public static WebApplicationBuilder AddSmashCourtTelemetry(this WebApplicationBuilder builder)
    {
        var settings = Read(builder.Configuration);
        if (!settings.Enabled)
            return builder;

        var endpoint = new Uri(settings.OtlpEndpoint);
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: settings.ServiceName,
                serviceVersion: Environment.GetEnvironmentVariable("APP_VERSION") ?? "local",
                serviceInstanceId: Environment.MachineName))
            .WithMetrics(metrics => metrics
                .AddSmashCourtMeters()
                .AddOtlpExporter((exporter, reader) =>
                {
                    ConfigureExporter(exporter, endpoint);
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = settings.MetricExportIntervalMs;
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(instrumentation =>
                {
                    // Docker healthcheck gọi mỗi 10 giây: không trace
                    instrumentation.Filter = context => !context.Request.Path.StartsWithSegments(HealthPathPrefix);
                    // Query của IPN VNPay chứa chữ ký: không đưa vào trace
                    instrumentation.EnrichWithHttpRequest = (activity, _) => activity.SetTag(TagUrlQuery, null);
                })
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .SetSampler(new ParentBasedSampler(new RequestRootSampler(settings.TraceSampleRatio)))
                .AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint)));

        if (settings.ExportLogs)
            telemetry.WithLogging(logging => logging.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint)),
                options => options.IncludeFormattedMessage = true);

        return builder;
    }

    /// <summary>Các meter dashboard "Vận hành" dùng; histogram thời gian request dùng <see cref="RequestDurationBoundariesSeconds"/>.</summary>
    public static MeterProviderBuilder AddSmashCourtMeters(this MeterProviderBuilder metrics) => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter(NpgsqlMeter)
        .AddView(RequestDurationInstrument, new ExplicitBucketHistogramConfiguration { Boundaries = [.. RequestDurationBoundariesSeconds] });

    private static TelemetrySettings Read(IConfiguration configuration)
    {
        var settings = configuration.GetSection(TelemetrySettings.SectionName).Get<TelemetrySettings>() ?? new TelemetrySettings();
        var result = new TelemetrySettingsValidator().Validate(Options.DefaultName, settings);
        if (result.Failed)
            throw new OptionsValidationException(Options.DefaultName, typeof(TelemetrySettings), result.Failures);
        return settings;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, Uri endpoint)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.Grpc;
    }
}
