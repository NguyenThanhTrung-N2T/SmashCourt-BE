using System.Text.Json.Nodes;
using SmashCourt_BE.Tests.TestData;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Kịch bản của TADSS giả (WireMock) cho <c>POST /api/v2/decisions</c> (CT-04). Response dựng từ payload mẫu đã kiểm theo JSON schema v2
/// (<c>TestData/Tadss/v2/samples</c>), chỉ thay <c>reservationId</c>/<c>decisionId</c> theo booking đang test để giữ đúng hình dạng mẫu.
/// </summary>
public sealed class TadssStub(WireMockServer server)
{
    public const string DecisionsPath = "/api/v2/decisions";

    /// <summary>Xóa mọi kịch bản và lịch sử request (gọi đầu mỗi test dùng TADSS giả).</summary>
    public void Reset() => server.Reset();

    /// <summary>Request đã nhận ở endpoint đánh giá.</summary>
    public IReadOnlyList<WireMock.Logging.ILogEntry> Evaluations =>
        server.LogEntries.Where(e => e.RequestMessage?.Path == DecisionsPath && e.RequestMessage.Method == "POST").ToList();

    /// <summary>
    /// Request đã nhận cho một booking (khóa idempotency của lần đánh giá là booking id). WireMock chỉ ghi log sau khi trả lời xong nên
    /// request của test khác có thể ghi muộn; lọc theo booking của chính test để không bị lẫn.
    /// </summary>
    public IReadOnlyList<WireMock.Logging.ILogEntry> EvaluationsFor(Guid bookingId) =>
        Evaluations.Where(e => e.RequestMessage!.Headers != null
                               && e.RequestMessage.Headers.TryGetValue("X-Idempotency-Key", out var key)
                               && key.Contains(bookingId.ToString())).ToList();

    /// <summary>
    /// Như <see cref="EvaluationsFor"/> nhưng chờ WireMock ghi xong log (nó ghi sau khi trả lời nên đọc ngay có thể thiếu): chờ tới khi đủ
    /// <paramref name="expected"/> request (tối đa 3 s). Khi mong đợi 0 thì chờ thêm một khoảng ngắn để request bị lọt (nếu có) kịp hiện ra.
    /// </summary>
    public async Task<IReadOnlyList<WireMock.Logging.ILogEntry>> WaitForEvaluationsAsync(Guid bookingId, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (EvaluationsFor(bookingId).Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        if (expected == 0)
            await Task.Delay(300);
        return EvaluationsFor(bookingId);
    }

    /// <summary>Dựng response từ mẫu, trả <c>decisionId</c> đã đặt.</summary>
    public Guid ReplyWithSample(string sampleFile, Guid reservationId, Action<JsonObject>? customize = null, Guid? decisionId = null)
    {
        var json = JsonNode.Parse(TadssContractFiles.Sample(sampleFile))!.AsObject();
        var id = decisionId ?? Guid.NewGuid();
        json["decisionId"] = id.ToString();
        json["reservationId"] = reservationId.ToString();
        customize?.Invoke(json);
        Reply(200, json.ToJsonString(), "application/json");
        return id;
    }

    public Guid ReplyApproved(Guid reservationId, decimal rate, decimal amount) =>
        ReplyWithSample("decision-result.approved.json", reservationId, json =>
        {
            json["deposit"] = new JsonObject { ["rate"] = rate, ["amount"] = amount, ["currency"] = "VND" };
        });

    public Guid ReplyNeedApproval(Guid reservationId, DateTimeOffset approvalExpiresAt) =>
        ReplyWithSample("decision-result.need-approval.json", reservationId, json =>
        {
            json["approvalExpiresAt"] = approvalExpiresAt.ToString("O");
        });

    public Guid ReplyRejected(Guid reservationId) => ReplyWithSample("decision-result.rejected.json", reservationId);

    public Guid ReplyFallback(Guid reservationId) => ReplyWithSample("decision-result.approved-fallback.json", reservationId);

    /// <summary>Trả một response tùy ý (lỗi 4xx/5xx, body hỏng...); <paramref name="retryAfter"/> là giá trị header Retry-After.</summary>
    public void Reply(int status, string? body, string contentType = "application/problem+json", string? retryAfter = null, TimeSpan? delay = null)
    {
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", contentType);
        if (body != null)
            response = response.WithBody(body);
        if (retryAfter != null)
            response = response.WithHeader("Retry-After", retryAfter);
        if (delay != null)
            response = response.WithDelay(delay.Value);

        server.Given(Request.Create().WithPath(DecisionsPath).UsingPost()).RespondWith(response);
    }

    /// <summary>TADSS chậm hơn nhiều so với <c>TimeoutMs</c> của host test (<see cref="SmashCourtWebApplicationFactory.TadssTimeoutMs"/>).</summary>
    public void ReplyTooSlowly() => Reply(200, "{}", "application/json", delay: TimeSpan.FromSeconds(5));
}
