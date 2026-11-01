using SmashCourt_BE.Configurations;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Configurations;

/// <summary>
/// OB-BE-1 (Epic 6C, gói 6C-3a): section <c>Telemetry</c>. Tắt (mặc định) thì không kiểm gì để dev và test không cần collector;
/// bật thì cấu hình sai phải dừng lúc khởi động, nêu đúng tên khóa.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "OB-BE-1")]
public class TelemetrySettingsValidatorTests
{
    private readonly TelemetrySettingsValidator _validator = new();

    private static TelemetrySettings Enabled() => new()
    {
        Enabled = true,
        OtlpEndpoint = "http://alloy:4317"
    };

    [Fact]
    public void Validate_WhenDefaults_IsDisabledAndValid()
    {
        var settings = new TelemetrySettings();

        var result = _validator.Validate(null, settings);

        Assert.False(settings.Enabled);
        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenDisabledWithInvalidValues_IsStillValid()
    {
        var settings = new TelemetrySettings { OtlpEndpoint = "khong-phai-url", MetricExportIntervalMs = 0, TraceSampleRatio = 5 };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenEnabledWithDefaults_IsValid()
    {
        var result = _validator.Validate(null, Enabled());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alloy:4317")]
    [InlineData("ftp://alloy:4317")]
    public void Validate_WhenEnabledWithInvalidEndpoint_FailsMentioningOtlpEndpoint(string endpoint)
    {
        var settings = Enabled();
        settings.OtlpEndpoint = endpoint;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Telemetry:OtlpEndpoint", result.FailureMessage);
    }

    [Theory]
    [InlineData(999)]
    [InlineData(300_001)]
    public void Validate_WhenEnabledWithIntervalOutOfRange_FailsMentioningInterval(int intervalMs)
    {
        var settings = Enabled();
        settings.MetricExportIntervalMs = intervalMs;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Telemetry:MetricExportIntervalMs", result.FailureMessage);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Validate_WhenEnabledWithSampleRatioOutOfRange_FailsMentioningRatio(double ratio)
    {
        var settings = Enabled();
        settings.TraceSampleRatio = ratio;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Telemetry:TraceSampleRatio", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenEnabledWithBlankServiceName_FailsMentioningServiceName()
    {
        var settings = Enabled();
        settings.ServiceName = " ";

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Telemetry:ServiceName", result.FailureMessage);
    }
}
