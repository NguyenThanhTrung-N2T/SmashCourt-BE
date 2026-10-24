using System.Text.Json.Nodes;
using SmashCourt_BE.Tests.TestData;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Settings;
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
    /// Request nhắc tới một sân (<c>resourceId</c> trong items), dùng khi test không có booking id (request bị chặn trước khi tạo đơn).
    /// Không đọc toàn bộ log: request chậm của test trước có thể được WireMock ghi muộn, sau khi test này đã reset.
    /// </summary>
    public IReadOnlyList<WireMock.Logging.ILogEntry> EvaluationsMentioning(Guid courtId) =>
        Evaluations.Where(e => e.RequestMessage?.Body?.Contains(courtId.ToString(), StringComparison.OrdinalIgnoreCase) == true).ToList();

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
    public void Reply(int status, string? body, string contentType = "application/problem+json", string? retryAfter = null, TimeSpan? delay = null) =>
        server.Given(Request.Create().WithPath(DecisionsPath).UsingPost())
            .RespondWith(BuildResponse(status, body, contentType, retryAfter, delay, template: false));

    private static IResponseBuilder BuildResponse(
        int status, string? body, string contentType, string? retryAfter, TimeSpan? delay, bool template)
    {
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", contentType);
        if (body != null)
            response = response.WithBody(body);
        if (retryAfter != null)
            response = response.WithHeader("Retry-After", retryAfter);
        if (delay != null)
            response = response.WithDelay(delay.Value);
        if (template)
            response = response.WithTransformer();
        return response;
    }

    // ───────── Kịch bản cho đặt sân qua HTTP: booking id chỉ biết sau transaction 1 ─────────

    private const string DecisionIdToken = "__decision_id__";
    private const string ReservationIdToken = "__reservation_id__";

    /// <summary>
    /// Body mẫu dùng được cho mọi booking (templating của WireMock): <c>decisionId</c> sinh mới mỗi lần trả (cột
    /// <c>bookings.tadss_decision_id</c> là unique), <c>reservationId</c> lấy lại từ request như TADSS thật.
    /// </summary>
    private static string TemplatedSample(string sampleFile, Action<JsonObject>? customize)
    {
        var json = JsonNode.Parse(TadssContractFiles.Sample(sampleFile))!.AsObject();
        customize?.Invoke(json);
        json["decisionId"] = DecisionIdToken;
        json["reservationId"] = ReservationIdToken;
        return json.ToJsonString()
            .Replace(DecisionIdToken, "{{Random Type=\"Guid\"}}")
            .Replace(ReservationIdToken, "{{JsonPath.SelectToken request.body \"$.reservationId\"}}");
    }

    /// <summary>Mọi booking nhận cùng một quyết định (decisionId khác nhau); <paramref name="delay"/> để mô phỏng TADSS chậm.</summary>
    public void ReplyForAnyBooking(string sampleFile, Action<JsonObject>? customize = null, TimeSpan? delay = null) =>
        server.Given(Request.Create().WithPath(DecisionsPath).UsingPost())
            .RespondWith(BuildResponse(200, TemplatedSample(sampleFile, customize), "application/json", null, delay, template: true));

    public void ApproveAnyBooking(decimal rate, decimal amount, TimeSpan? delay = null) =>
        ReplyForAnyBooking("decision-result.approved.json", json =>
            json["deposit"] = new JsonObject { ["rate"] = rate, ["amount"] = amount, ["currency"] = "VND" }, delay);

    public void RequireApprovalForAnyBooking(DateTimeOffset approvalExpiresAt, TimeSpan? delay = null) =>
        ReplyForAnyBooking("decision-result.need-approval.json", json =>
            json["approvalExpiresAt"] = approvalExpiresAt.ToString("O"), delay);

    public void RejectAnyBooking(TimeSpan? delay = null) => ReplyForAnyBooking("decision-result.rejected.json", delay: delay);

    /// <summary>
    /// Z-13: lần gọi đầu trả chậm hơn <paramref name="firstDelay"/> (vượt TimeoutMs), lần sau trả ngay quyết định mẫu (kịch bản trạng thái
    /// của WireMock: lần đầu chuyển trạng thái nên lần thử lại của client khớp mapping thứ hai).
    /// </summary>
    public void ReplyTooSlowlyOnceThen(string sampleFile, TimeSpan firstDelay, Action<JsonObject>? customize = null)
    {
        const string scenario = "first-attempt-times-out";
        var body = TemplatedSample(sampleFile, customize);
        server.Given(Request.Create().WithPath(DecisionsPath).UsingPost())
            .InScenario(scenario)
            .WillSetStateTo("answered")
            .RespondWith(BuildResponse(200, body, "application/json", null, firstDelay, template: true));
        server.Given(Request.Create().WithPath(DecisionsPath).UsingPost())
            .InScenario(scenario)
            .WhenStateIs("answered")
            .RespondWith(BuildResponse(200, body, "application/json", null, null, template: true));
    }

    /// <summary>
    /// Fault <c>EMPTY_RESPONSE</c> của WireMock.Net: trong tiến trình nó trả 200 body rỗng (không cắt được kết nối TCP), nên với BE đây
    /// là phản hồi không dùng được, không phải lỗi mạng. Lỗi mạng thật: trỏ <c>TadssConfig:BaseUrl</c> sang cổng đóng.
    /// </summary>
    public void FailWithNetworkFault() =>
        server.Given(Request.Create().WithPath(DecisionsPath).UsingPost())
            .RespondWith(Response.Create().WithFault(FaultType.EMPTY_RESPONSE));

    /// <summary>TADSS chậm hơn nhiều so với <c>TimeoutMs</c> của host test (<see cref="SmashCourtWebApplicationFactory.TadssTimeoutMs"/>).</summary>
    public void ReplyTooSlowly() => Reply(200, "{}", "application/json", delay: TimeSpan.FromSeconds(5));
}
