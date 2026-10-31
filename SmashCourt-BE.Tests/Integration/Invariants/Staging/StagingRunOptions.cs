using System.Globalization;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>
/// Cấu hình một lượt kiểm SC6 trên staging (plan 6A-6 Task 11), đọc từ biến môi trường: <c>SC6_CONNECTION_STRING</c> (nên dùng role chỉ đọc),
/// <c>SC6_FROM_UTC</c>, <c>SC6_TO_UTC</c> (ISO 8601, bắt buộc), <c>TADSS_BASE_URL</c>, <c>TADSS_API_KEY</c>, <c>SC6_OUTPUT</c> (đường dẫn JSON).
/// <see cref="TadssHandler"/>, <see cref="DecisionTimeout"/>, <see cref="Clock"/> chỉ để test.
/// </summary>
public sealed record StagingRunOptions(
    string ConnectionString,
    DateTime FromUtc,
    DateTime ToUtc,
    Uri? TadssBaseUrl,
    string? TadssApiKey,
    string? OutputPath)
{
    public const string ConnectionStringVariable = "SC6_CONNECTION_STRING";

    public HttpMessageHandler? TadssHandler { get; init; }
    public TimeSpan? DecisionTimeout { get; init; }
    public Func<DateTime>? Clock { get; init; }

    public static StagingRunOptions FromEnvironment()
    {
        static string? Read(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

        var connectionString = Read(ConnectionStringVariable)
            ?? throw new InvalidOperationException($"Thiếu {ConnectionStringVariable}.");
        var tadss = Read("TADSS_BASE_URL");
        return new StagingRunOptions(
            connectionString,
            ReadUtc(Read("SC6_FROM_UTC"), "SC6_FROM_UTC"),
            ReadUtc(Read("SC6_TO_UTC"), "SC6_TO_UTC"),
            tadss == null ? null : new Uri(tadss.EndsWith('/') ? tadss : tadss + "/"),
            Read("TADSS_API_KEY"),
            Read("SC6_OUTPUT"));
    }

    // ISO 8601 có offset hoặc Z; quy về UTC
    private static DateTime ReadUtc(string? value, string name)
    {
        if (value == null)
            throw new InvalidOperationException($"Thiếu {name} (ISO 8601, ví dụ 2026-12-01T02:00:00Z): tập dữ liệu kiểm phải cố định.");
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            throw new InvalidOperationException($"{name} = '{value}' không phải thời điểm ISO 8601.");
        return parsed.UtcDateTime;
    }
}
