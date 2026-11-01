using Microsoft.Extensions.Options;

namespace SmashCourt_BE.Configurations;

/// <summary>
/// Kiểm tra <see cref="TelemetrySettings"/> lúc khởi động. Chỉ kiểm khi bật để dev và test (tắt) không cần collector.
/// </summary>
public class TelemetrySettingsValidator : IValidateOptions<TelemetrySettings>
{
    public const int MinExportIntervalMs = 1000;
    public const int MaxExportIntervalMs = 300_000;

    public ValidateOptionsResult Validate(string? name, TelemetrySettings options)
    {
        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var errors = new List<string>();

        if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            errors.Add($"{TelemetrySettings.SectionName}:OtlpEndpoint phải là URL http/https tuyệt đối khi Enabled bật (hiện '{options.OtlpEndpoint}').");

        if (string.IsNullOrWhiteSpace(options.ServiceName))
            errors.Add($"{TelemetrySettings.SectionName}:ServiceName không được để trống.");

        if (options.MetricExportIntervalMs is < MinExportIntervalMs or > MaxExportIntervalMs)
            errors.Add($"{TelemetrySettings.SectionName}:MetricExportIntervalMs phải trong khoảng {MinExportIntervalMs}…{MaxExportIntervalMs} (hiện {options.MetricExportIntervalMs}).");

        if (options.TraceSampleRatio is < 0 or > 1 || double.IsNaN(options.TraceSampleRatio))
            errors.Add($"{TelemetrySettings.SectionName}:TraceSampleRatio phải trong khoảng 0…1 (hiện {options.TraceSampleRatio}).");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
