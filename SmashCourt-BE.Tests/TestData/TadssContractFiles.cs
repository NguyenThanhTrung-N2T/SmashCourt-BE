using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>
/// Nạp JSON schema v2 và payload mẫu của TADSS (bản sao trong <c>TestData/Tadss/v2</c>) rồi đánh giá payload.
/// </summary>
internal static class TadssContractFiles
{
    public const string DecisionRequest = "decision-request.schema.json";
    public const string DecisionResult = "decision-result.schema.json";
    public const string DecisionDetail = "decision-detail.schema.json";
    public const string OverrideRequest = "override-request.schema.json";
    public const string OverrideResponse = "override-response.schema.json";
    public const string OutcomeRequest = "outcome-request.schema.json";
    public const string OutcomeResponse = "outcome-response.schema.json";

    private static readonly ConcurrentDictionary<string, JsonSchema> Cache = new(StringComparer.Ordinal);

    // JsonSchema.Net đăng ký $id vào registry toàn cục ở lần đánh giá đầu; các class test chạy song song
    // cùng gọi lần đầu sẽ lỗi "Overwriting registered schemas", nên đánh giá được tuần tự hóa
    private static readonly object EvaluationLock = new();

    public static string Root => Path.Combine(AppContext.BaseDirectory, "TestData", "Tadss", "v2");

    public static string Sample(string fileName) => File.ReadAllText(Path.Combine(Root, "samples", fileName));

    public static EvaluationResults Evaluate(string schemaFile, string json)
    {
        var instance = JsonSerializer.Deserialize<JsonElement>(json);
        lock (EvaluationLock)
        {
            var schema = Cache.GetOrAdd(schemaFile, file =>
                JsonSchema.FromText(File.ReadAllText(Path.Combine(Root, "schemas", file))));
            return schema.Evaluate(instance, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            });
        }
    }

    /// <summary>Mô tả lỗi schema để hiển thị khi assertion thất bại.</summary>
    public static string Describe(EvaluationResults results) =>
        string.Join("; ", (results.Details ?? [])
            .Where(detail => !detail.IsValid)
            .SelectMany(detail => (detail.Errors ?? new Dictionary<string, string>())
                .Select(error => $"{detail.InstanceLocation}: {error.Value}")));
}
