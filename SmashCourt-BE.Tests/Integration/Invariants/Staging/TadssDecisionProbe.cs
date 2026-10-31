using System.Net;
using System.Text.Json;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>Kết quả tra một quyết định: có ở TADSS, chắc chắn không có (vi phạm SC6-5), hoặc không kết luận được.</summary>
public enum DecisionCheckOutcome
{
    Found,
    Missing,
    Inconclusive
}

public sealed record DecisionCheck(Guid BookingId, Guid DecisionId, DecisionCheckOutcome Outcome, string? Reason);

/// <summary>
/// SC6-5 (plan 6A-6 Task 11): mỗi <c>tadss_decision_id</c> lưu ở BE phải tra được qua <c>GET /api/v2/decisions/{id}</c> (INTEGRATION_SPEC §3.4).
/// Chỉ 404 <c>application/problem+json</c> có <c>code = DECISION_NOT_FOUND</c> (§3.8) là vi phạm; mọi trường hợp khác (401, 429, 5xx, timeout,
/// lỗi mạng, 404 không đúng dạng, 200 sai id) là không kết luận được: lỗi gọi API không bao giờ được tính là đạt.
/// <see cref="HttpClient"/> do caller dựng (BaseAddress, <c>X-Api-Key</c>), timeout của chính client nên để lớn hơn timeout từng request.
/// </summary>
public static class TadssDecisionProbe
{
    public const int DefaultMaxConcurrency = 8;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private const string NotFoundCode = "DECISION_NOT_FOUND";

    public static async Task<IReadOnlyList<DecisionCheck>> CheckAsync(
        HttpClient client, IReadOnlyCollection<(Guid BookingId, Guid DecisionId)> decisions,
        int maxConcurrency = DefaultMaxConcurrency, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);
        var perRequest = timeout ?? DefaultTimeout;

        using var gate = new SemaphoreSlim(maxConcurrency);
        // Mỗi task trả kết quả của mình rồi gộp: không có collection dùng chung giữa các luồng
        var tasks = decisions.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await CheckOneAsync(client, d.BookingId, d.DecisionId, perRequest, ct);
            }
            finally
            {
                gate.Release();
            }
        });
        return await Task.WhenAll(tasks);
    }

    private static async Task<DecisionCheck> CheckOneAsync(
        HttpClient client, Guid bookingId, Guid decisionId, TimeSpan timeout, CancellationToken ct)
    {
        DecisionCheck Result(DecisionCheckOutcome outcome, string? reason = null) => new(bookingId, decisionId, outcome, reason);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            using var response = await client.GetAsync($"api/v2/decisions/{decisionId}", timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var mediaType = response.Content.Headers.ContentType?.MediaType;

            return response.StatusCode switch
            {
                HttpStatusCode.OK => ReadDecisionId(body) == decisionId
                    ? Result(DecisionCheckOutcome.Found)
                    : Result(DecisionCheckOutcome.Inconclusive, "200 nhưng body không có đúng decisionId"),
                HttpStatusCode.NotFound when mediaType == "application/problem+json" && ReadCode(body) == NotFoundCode =>
                    Result(DecisionCheckOutcome.Missing, $"404 {NotFoundCode}"),
                HttpStatusCode.NotFound =>
                    Result(DecisionCheckOutcome.Inconclusive, $"404 không đúng dạng problem+json {NotFoundCode} (Content-Type {mediaType ?? "trống"})"),
                _ => Result(DecisionCheckOutcome.Inconclusive, $"HTTP {(int)response.StatusCode}")
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result(DecisionCheckOutcome.Inconclusive, $"quá {timeout.TotalSeconds:0.#} giây không phản hồi");
        }
        catch (HttpRequestException ex)
        {
            return Result(DecisionCheckOutcome.Inconclusive, $"lỗi mạng: {ex.Message}");
        }
    }

    private static Guid? ReadDecisionId(string body) =>
        TryRead(body, "decisionId") is { } value && Guid.TryParse(value, out var id) ? id : null;

    private static string? ReadCode(string body) => TryRead(body, "code");

    private static string? TryRead(string body, string property)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty(property, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
