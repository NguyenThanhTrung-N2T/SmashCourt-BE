using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integrations;

/// <summary>BE-UT-01: <see cref="TadssClient"/> (header, timeout, thử lại, phân loại lỗi, log) với <see cref="HttpMessageHandler"/> giả.</summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-01")]
public class TadssClientTests
{
    private const string ApiKey = "SECRET-KEY-123";

    private static string ApprovedJson => TadssContractFiles.Sample("decision-result.approved.json");

    private sealed record Sut(
        TadssClient Client, FakeHttpMessageHandler Handler, ListLogger<TadssClient> Logger, TadssSettings Settings, HttpContextAccessor Accessor);

    private static Sut Create(FakeHttpMessageHandler handler, Action<TadssSettings>? configure = null)
    {
        var settings = new TadssSettings { EnableTadssDecision = true, BaseUrl = "http://tadss.test:5296", ApiKey = ApiKey, TimeoutMs = 800 };
        configure?.Invoke(settings);
        var monitor = new Mock<IOptionsMonitor<TadssSettings>>();
        monitor.Setup(x => x.CurrentValue).Returns(settings);
        var logger = new ListLogger<TadssClient>();
        var accessor = new HttpContextAccessor();
        var client = new TadssClient(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, monitor.Object, accessor, TimeProvider.System, logger);
        return new Sut(client, handler, logger, settings, accessor);
    }

    private static Sut CreateReplying(Func<HttpResponseMessage> response, Action<TadssSettings>? configure = null) =>
        Create(FakeHttpMessageHandler.Always(response), configure);

    private static Task<TadssCallResult<TadssDecisionResultDto>> Evaluate(Sut sut, CancellationToken token = default) =>
        sut.Client.EvaluateAsync(TadssTestData.DecisionRequest(full: true), TadssTestData.BookingId, token);

    // ── Header và đường dẫn ──────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenCalled_SendsRequiredHeadersBodyAndPath()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));

        var result = await Evaluate(sut);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(sut.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://tadss.test:5296/api/v2/decisions", request.Uri.ToString());
        Assert.Equal(ApiKey, request.Header("X-Api-Key"));
        Assert.Equal(TadssTestData.BookingId.ToString(), request.Header("X-Idempotency-Key"));
        Assert.Equal("true", request.Header("ngrok-skip-browser-warning"));
        Assert.False(string.IsNullOrWhiteSpace(request.Header("X-Correlation-ID")));
        Assert.Null(request.Header("X-Actor-Id"));
        Assert.Equal("application/json", request.ContentType);
        var body = JsonNode.Parse(request.Body!)!;
        Assert.Equal(TadssTestData.BookingId.ToString(), body["reservationId"]!.GetValue<string>());
        Assert.Equal("ONLINE", body["channel"]!.GetValue<string>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenSucceeds_ReturnsParsedValueAndStatus()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));

        var result = await Evaluate(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal(TadssDecisionStatus.APPROVED, result.Value.Status);
        Assert.Equal(114000m, result.Value.Deposit!.Amount);
        Assert.Equal(200, result.HttpStatus);
        Assert.Null(result.FailureKind);
    }

    [Theory]
    [InlineData(TadssOverrideAction.APPROVE)]
    [InlineData(TadssOverrideAction.REJECT)]
    public async Task OverrideAsync_WhenCalled_SendsActorIdAndActionScopedIdempotencyKey(TadssOverrideAction action)
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(
            HttpStatusCode.OK, TadssContractFiles.Sample("override-response.approve.json")));
        var request = action == TadssOverrideAction.APPROVE ? TadssTestData.OverrideApprove() : TadssTestData.OverrideReject();
        var actorId = Guid.NewGuid().ToString();

        var result = await sut.Client.OverrideAsync(TadssTestData.DecisionId, request, actorId);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(sut.Handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"http://tadss.test:5296/api/v2/decisions/{TadssTestData.DecisionId}/override", sent.Uri.ToString());
        Assert.Equal(actorId, sent.Header("X-Actor-Id"));
        Assert.Equal($"{TadssTestData.DecisionId}:{action}", sent.Header("X-Idempotency-Key"));
        Assert.Equal(ApiKey, sent.Header("X-Api-Key"));
        Assert.Equal(action == TadssOverrideAction.APPROVE, JsonNode.Parse(sent.Body!)!.AsObject().ContainsKey("deposit"));
    }

    [Fact]
    public async Task GetDecisionAsync_WhenCalled_SendsGetWithoutBodyOrIdempotencyKey()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TadssContractFiles.Sample("decision-detail.json")));

        var result = await sut.Client.GetDecisionAsync(TadssTestData.DecisionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(TadssOutcome.COMPLETED, result.Value.Outcome!.Outcome);
        var sent = Assert.Single(sut.Handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal($"http://tadss.test:5296/api/v2/decisions/{TadssTestData.DecisionId}", sent.Uri.ToString());
        Assert.Null(sent.Body);
        Assert.Null(sent.Header("X-Idempotency-Key"));
        Assert.Null(sent.Header("X-Actor-Id"));
        Assert.Equal(ApiKey, sent.Header("X-Api-Key"));
    }

    [Fact]
    public async Task ReportOutcomeAsync_WhenCalled_SendsOutcomeScopedIdempotencyKey()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, TadssContractFiles.Sample("outcome-response.json")));

        var result = await sut.Client.ReportOutcomeAsync(TadssTestData.DecisionId, TadssTestData.OutcomeNoShow());

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(sut.Handler.Requests);
        Assert.Equal($"http://tadss.test:5296/api/v2/decisions/{TadssTestData.DecisionId}/outcome", sent.Uri.ToString());
        Assert.Equal($"{TadssTestData.DecisionId}:OUTCOME", sent.Header("X-Idempotency-Key"));
        Assert.Null(sent.Header("X-Actor-Id"));
        Assert.Equal("NO_SHOW", JsonNode.Parse(sent.Body!)!["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task EvaluateAsync_WhenSettingsChangeBetweenCalls_UsesLatestApiKeyAndBaseUrl()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));
        await Evaluate(sut);

        sut.Settings.ApiKey = "ROTATED-KEY";
        sut.Settings.BaseUrl = "https://tadss.example/prefix/";
        await Evaluate(sut);

        Assert.Equal(ApiKey, sut.Handler.Requests[0].Header("X-Api-Key"));
        Assert.Equal("ROTATED-KEY", sut.Handler.Requests[1].Header("X-Api-Key"));
        Assert.Equal("https://tadss.example/prefix/api/v2/decisions", sut.Handler.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task EvaluateAsync_WhenBaseUrlIsInvalid_ReturnsNetworkFailureWithoutCallingOut()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson), s => s.BaseUrl = "chưa cấu hình");

        var result = await Evaluate(sut);

        Assert.False(result.IsSuccess);
        Assert.Equal(TadssFailureKind.Network, result.FailureKind);
        Assert.Empty(sut.Handler.Requests);
    }

    // ── Correlation id ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenIncomingRequestHasCorrelationHeader_ReusesIt()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));
        var context = new DefaultHttpContext { TraceIdentifier = "trace-xyz" };
        context.Request.Headers["X-Correlation-ID"] = "corr-from-fe";
        sut.Accessor.HttpContext = context;

        var result = await Evaluate(sut);

        Assert.Equal("corr-from-fe", sut.Handler.Requests[0].Header("X-Correlation-ID"));
        Assert.Equal("corr-from-fe", result.CorrelationId);
    }

    [Fact]
    public async Task EvaluateAsync_WhenIncomingRequestHasNoCorrelationHeader_UsesTraceIdentifier()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));
        sut.Accessor.HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-xyz" };

        await Evaluate(sut);

        Assert.Equal("trace-xyz", sut.Handler.Requests[0].Header("X-Correlation-ID"));
    }

    [Theory]
    [InlineData("tiếng-việt")]
    [InlineData("has space")]
    public async Task EvaluateAsync_WhenIncomingCorrelationHeaderIsNotVisibleAscii_FallsBackToTraceIdentifier(string incoming)
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));
        var context = new DefaultHttpContext { TraceIdentifier = "trace-xyz" };
        context.Request.Headers["X-Correlation-ID"] = incoming;
        sut.Accessor.HttpContext = context;

        var result = await Evaluate(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal("trace-xyz", sut.Handler.Requests[0].Header("X-Correlation-ID"));
    }

    [Fact]
    public async Task EvaluateAsync_WhenNoUsableCorrelationIdInRequest_GeneratesGuid()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));
        var context = new DefaultHttpContext { TraceIdentifier = new string('x', 129) };
        context.Request.Headers["X-Correlation-ID"] = new string('y', 129);
        sut.Accessor.HttpContext = context;

        await Evaluate(sut);

        Assert.True(Guid.TryParse(sut.Handler.Requests[0].Header("X-Correlation-ID"), out _));
    }

    [Fact]
    public async Task EvaluateAsync_WhenRetried_KeepsSameCorrelationIdAcrossAttempts()
    {
        var handler = new FakeHttpMessageHandler((n, _) => Task.FromResult(n == 1
            ? FakeHttpMessageHandler.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson)));
        var sut = Create(handler);

        await Evaluate(sut);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Header("X-Correlation-ID"), handler.Requests[1].Header("X-Correlation-ID"));
    }

    // ── Timeout ──────────────────────────────────────────────────────────────────

    private static FakeHttpMessageHandler NeverRespond() => new(async (_, request) =>
    {
        await Task.Delay(Timeout.Infinite, request.Token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    });

    [Fact]
    public async Task EvaluateAsync_WhenTadssNeverResponds_CancelsEachAttemptAtTimeoutAndRetriesOnce()
    {
        var sut = Create(NeverRespond(), s => s.TimeoutMs = 150);
        var stopwatch = Stopwatch.StartNew();

        var result = await Evaluate(sut);

        stopwatch.Stop();
        Assert.False(result.IsSuccess);
        Assert.Equal(TadssFailureKind.Timeout, result.FailureKind);
        Assert.Equal(2, sut.Handler.Requests.Count);
        Assert.All(sut.Handler.Requests, r => Assert.True(r.Token.IsCancellationRequested));
        Assert.Equal(sut.Handler.Requests[0].Header("X-Idempotency-Key"), sut.Handler.Requests[1].Header("X-Idempotency-Key"));
        Assert.InRange(stopwatch.ElapsedMilliseconds, 250, 5000); // hai lần × 150 ms, không bị treo
    }

    [Fact]
    public async Task GetDecisionAsync_WhenTadssNeverResponds_TimesOutWithoutRetry()
    {
        var sut = Create(NeverRespond(), s => s.TimeoutMs = 150);

        var result = await sut.Client.GetDecisionAsync(TadssTestData.DecisionId);

        Assert.Equal(TadssFailureKind.Timeout, result.FailureKind);
        Assert.Single(sut.Handler.Requests);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCallerCancels_ThrowsWithoutRetryAndWithoutTimeoutLog()
    {
        var sut = Create(NeverRespond(), s => s.TimeoutMs = 5000);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Evaluate(sut, cts.Token));

        Assert.Single(sut.Handler.Requests);
        Assert.DoesNotContain(sut.Logger.Entries, e => e.Message.Contains("quá thời gian"));
    }

    // ── Thử lại ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task EvaluateAsync_WhenFirstAttemptIs5xx_RetriesOnceWithSameKeyAndBody(HttpStatusCode firstStatus)
    {
        var handler = new FakeHttpMessageHandler((n, _) => Task.FromResult(n == 1
            ? FakeHttpMessageHandler.Problem(firstStatus, "INTERNAL_ERROR")
            : FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson)));
        var sut = Create(handler);

        var result = await Evaluate(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(TadssTestData.BookingId.ToString(), handler.Requests[0].Header("X-Idempotency-Key"));
        Assert.Equal(handler.Requests[0].Header("X-Idempotency-Key"), handler.Requests[1].Header("X-Idempotency-Key"));
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
    }

    [Fact]
    public async Task EvaluateAsync_WhenFirstAttemptIsNetworkError_RetriesOnce()
    {
        var handler = new FakeHttpMessageHandler((n, _) => n == 1
            ? throw new HttpRequestException("connection refused")
            : Task.FromResult(FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson)));
        var sut = Create(handler);

        var result = await Evaluate(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task EvaluateAsync_WhenNetworkErrorPersists_StopsAfterTwoAttemptsWithNetworkFailure()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));
        var sut = Create(handler);

        var result = await Evaluate(sut);

        Assert.Equal(TadssFailureKind.Network, result.FailureKind);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task EvaluateAsync_When5xxPersists_StopsAfterTwoAttemptsWithHttpFailure()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, "PERSISTENCE_UNAVAILABLE", "5"));

        var result = await Evaluate(sut);

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Equal(503, result.HttpStatus);
        Assert.Equal("PERSISTENCE_UNAVAILABLE", result.ErrorCode);
        Assert.Equal(TimeSpan.FromSeconds(5), result.RetryAfter);
        Assert.Equal(2, sut.Handler.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "VALIDATION_ERROR")]
    [InlineData(HttpStatusCode.Unauthorized, "UNAUTHORIZED")]
    [InlineData(HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSE")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "PAYLOAD_TOO_LARGE")]
    [InlineData((HttpStatusCode)429, "RATE_LIMITED")]
    public async Task EvaluateAsync_When4xx_ReturnsHttpFailureWithCodeAndDoesNotRetry(HttpStatusCode status, string code)
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Problem(status, code));

        var result = await Evaluate(sut);

        Assert.False(result.IsSuccess);
        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Equal((int)status, result.HttpStatus);
        Assert.Equal(code, result.ErrorCode);
        Assert.Single(sut.Handler.Requests);
    }

    [Fact]
    public async Task EvaluateAsync_When415WithoutCode_ClassifiesByStatusAndDoesNotRetry()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(
            HttpStatusCode.UnsupportedMediaType, """{"title":"Unsupported Media Type","status":415,"traceId":"t"}""", "application/problem+json"));

        var result = await Evaluate(sut);

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Equal(415, result.HttpStatus);
        Assert.Null(result.ErrorCode);
        Assert.Single(sut.Handler.Requests);
    }

    [Theory]
    [InlineData("Override")]
    [InlineData("GetDecision")]
    [InlineData("ReportOutcome")]
    public async Task OtherOperations_When5xx_DoNotRetry(string operation)
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR"));

        TadssFailureKind? kind = operation switch
        {
            "Override" => (await sut.Client.OverrideAsync(TadssTestData.DecisionId, TadssTestData.OverrideReject(), "actor")).FailureKind,
            "GetDecision" => (await sut.Client.GetDecisionAsync(TadssTestData.DecisionId)).FailureKind,
            _ => (await sut.Client.ReportOutcomeAsync(TadssTestData.DecisionId, TadssTestData.OutcomeNoShow())).FailureKind
        };

        Assert.Equal(TadssFailureKind.Http, kind);
        Assert.Single(sut.Handler.Requests);
    }

    [Fact]
    public async Task OverrideAsync_WhenAlreadyOverridden_ReturnsConflictCodeWithoutRetry()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Problem(HttpStatusCode.Conflict, "ALREADY_OVERRIDDEN"));

        var result = await sut.Client.OverrideAsync(TadssTestData.DecisionId, TadssTestData.OverrideApprove(), "actor");

        Assert.Equal("ALREADY_OVERRIDDEN", result.ErrorCode);
        Assert.Equal(409, result.HttpStatus);
        Assert.Single(sut.Handler.Requests);
    }

    // ── Retry-After ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_When429WithRetryAfterHeader_ReturnsRetryAfter()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Problem((HttpStatusCode)429, "RATE_LIMITED", "2"));

        var result = await Evaluate(sut);

        Assert.Equal(TimeSpan.FromSeconds(2), result.RetryAfter);
    }

    [Fact]
    public async Task EvaluateAsync_When429OnlyBodyHasRetryAfterSeconds_FallsBackToBody()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(
            (HttpStatusCode)429, """{"code":"RATE_LIMITED","status":429,"retryAfterSeconds":3}""", "application/problem+json"));

        var result = await Evaluate(sut);

        Assert.Equal(TimeSpan.FromSeconds(3), result.RetryAfter);
    }

    [Fact]
    public async Task EvaluateAsync_WhenErrorBodyIsNotJson_ClassifiesByStatusOnly()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "text/html"));

        var result = await Evaluate(sut);

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Equal(502, result.HttpStatus);
        Assert.Null(result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 1)]
    [InlineData(HttpStatusCode.BadGateway, 2)]
    public async Task EvaluateAsync_WhenErrorBodyIsEmpty_ClassifiesByStatusOnly(HttpStatusCode status, int expectedAttempts)
    {
        // Proxy/ngrok trả lỗi không có body: không đọc problem nhưng vẫn phân loại và thử lại theo mã HTTP
        var sut = CreateReplying(() => new HttpResponseMessage(status) { Content = new StringContent("") });

        var result = await Evaluate(sut);

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Equal((int)status, result.HttpStatus);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.RetryAfter);
        Assert.Equal(expectedAttempts, sut.Handler.Requests.Count);
    }

    private static HttpResponseMessage TooManyRequests(Action<System.Net.Http.Headers.HttpResponseHeaders> headers, string body = "")
    {
        var response = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent(body) };
        headers(response.Headers);
        return response;
    }

    [Fact]
    public async Task EvaluateAsync_When429RetryAfterIsFutureHttpDate_ReturnsRemainingWait()
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(10);
        var sut = CreateReplying(() => TooManyRequests(h => h.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(at)));

        var result = await Evaluate(sut);

        // Header dạng ngày chỉ chính xác tới giây; thời gian chạy test nằm trong khoảng cho phép
        Assert.NotNull(result.RetryAfter);
        Assert.InRange(result.RetryAfter.Value, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task EvaluateAsync_When429RetryAfterIsPastHttpDate_ReturnsZeroNotNegative()
    {
        var sut = CreateReplying(() => TooManyRequests(h =>
            h.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-5))));

        var result = await Evaluate(sut);

        Assert.Equal(TimeSpan.Zero, result.RetryAfter);
    }

    [Fact]
    public async Task EvaluateAsync_WhenRetryAfterInHeaderAndBody_HeaderWins()
    {
        var sut = CreateReplying(() => TooManyRequests(
            h => h.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2)),
            """{"code":"RATE_LIMITED","status":429,"retryAfterSeconds":9}"""));

        var result = await Evaluate(sut);

        Assert.Equal(TimeSpan.FromSeconds(2), result.RetryAfter);
        Assert.Equal("RATE_LIMITED", result.ErrorCode);
    }

    // ── Phản hồi 2xx không dùng được ─────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_When2xxBodyIsUnusableWithoutContentType_LogsDashForContentType()
    {
        var sut = CreateReplying(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("oops"u8.ToArray()) });

        var result = await Evaluate(sut);

        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        Assert.Contains("ContentType=-", Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error).Message);
    }

    public static IEnumerable<object[]> InvalidSuccessBodies()
    {
        yield return ["html của ngrok", "<html><body>You are about to visit a site</body></html>", "text/html"];
        yield return ["JSON hỏng", """{"decisionId": "0f9c""", "application/json"];
        yield return ["body rỗng", "", "application/json"];
        yield return ["null", "null", "application/json"];
        yield return ["status lạ", ApprovedJson.Replace("\"APPROVED\"", "\"MAYBE\""), "application/json"];
        yield return ["thiếu trường bắt buộc", ApprovedJson.Replace("\"decisionId\"", "\"decisionIdX\""), "application/json"];
        yield return ["APPROVED thiếu deposit", ApprovedJson.Replace("\"deposit\": { \"rate\": 0.5, \"amount\": 114000, \"currency\": \"VND\" }", "\"deposit\": null"), "application/json"];
        // F1: vi phạm phạm vi của decision-result.schema.json (rate ∈ [0, 1], amount ≥ 0)
        yield return ["deposit.rate > 1", ApprovedJson.Replace("\"rate\": 0.5", "\"rate\": 1.5"), "application/json"];
        yield return ["deposit.rate âm", ApprovedJson.Replace("\"rate\": 0.5", "\"rate\": -0.1"), "application/json"];
        yield return ["deposit.amount âm", ApprovedJson.Replace("\"amount\": 114000", "\"amount\": -1"), "application/json"];
        // Quyết định của booking khác (VD TADSS trả nhầm bản đã lưu khi lỗi idempotency): không được áp lên đơn đang đặt
        yield return ["reservationId của booking khác",
            ApprovedJson.Replace("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11", "00000000-0000-4000-8000-0000000000ff"), "application/json"];
        yield return ["NEED_APPROVAL của booking khác",
            TadssContractFiles.Sample("decision-result.need-approval.json").Replace("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11", "00000000-0000-4000-8000-0000000000ff"),
            "application/json"];
        yield return ["REJECTED của booking khác",
            TadssContractFiles.Sample("decision-result.rejected.json").Replace("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11", "00000000-0000-4000-8000-0000000000ff"),
            "application/json"];
        yield return ["NEED_APPROVAL thiếu approvalExpiresAt",
            TadssContractFiles.Sample("decision-result.need-approval.json").Replace("\"approvalExpiresAt\": \"2026-11-18T14:15:00+07:00\"", "\"approvalExpiresAt\": null"),
            "application/json"];
    }

    [Theory]
    [MemberData(nameof(InvalidSuccessBodies))]
    public async Task EvaluateAsync_When2xxBodyIsUnusable_ReturnsInvalidResponseWithoutRetry(string name, string body, string contentType)
    {
        Assert.NotEmpty(name);
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body, contentType));

        var result = await Evaluate(sut);

        Assert.False(result.IsSuccess);
        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        Assert.Equal(200, result.HttpStatus);
        Assert.Single(sut.Handler.Requests);
        Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1", "228000")]
    [InlineData("0.0001", "23")]
    public async Task EvaluateAsync_WhenDepositIsAtTheSchemaBoundaries_IsAccepted(string rate, string amount)
    {
        var body = ApprovedJson.Replace("\"rate\": 0.5", $"\"rate\": {rate}").Replace("\"amount\": 114000", $"\"amount\": {amount}");
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body, "application/json"));

        var result = await Evaluate(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), result.Value.Deposit!.Rate);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationIdDiffers_LogsBothIdsForInvestigation()
    {
        var other = Guid.Parse("00000000-0000-4000-8000-0000000000ff");
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
            ApprovedJson.Replace(TadssTestData.BookingId.ToString(), other.ToString()), "application/json"));

        var result = await Evaluate(sut);

        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        var entry = Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(other.ToString(), entry.Message);
        Assert.Contains(TadssTestData.BookingId.ToString(), entry.Message);
        Assert.Contains("reservationId", entry.Message);
    }

    [Fact]
    public async Task EvaluateAsync_WhenReservationIdMatchesInUpperCase_IsAccepted()
    {
        // So theo GUID, không theo chuỗi: TADSS viết hoa vẫn là cùng booking
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
            ApprovedJson.Replace(TadssTestData.BookingId.ToString(), TadssTestData.BookingId.ToString().ToUpperInvariant()), "application/json"));

        var result = await Evaluate(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal(TadssTestData.BookingId, result.Value.ReservationId);
    }

    [Fact]
    public async Task EvaluateAsync_ComparesWithTheReservationIdOfTheRequest_NotOnlyTheIdempotencyKey()
    {
        // Request và khóa idempotency luôn cùng booking id; kiểm theo request để không phụ thuộc cách dựng khóa
        var request = TadssTestData.DecisionRequest(full: true);
        var reservation = Guid.NewGuid();
        request.ReservationId = reservation;
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
            ApprovedJson.Replace(TadssTestData.BookingId.ToString(), reservation.ToString()), "application/json"));

        var result = await sut.Client.EvaluateAsync(request, reservation);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EvaluateAsync_When2xxBodyIsUnusable_LogsErrorWithStatusContentTypeLengthAndFirst200Chars()
    {
        var body = new string('A', 150) + new string('B', 350);
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body, "text/html"));

        var result = await Evaluate(sut);

        var entry = Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains($"CorrelationId={result.CorrelationId}", entry.Message);
        Assert.Contains($"IdempotencyKey={TadssTestData.BookingId}", entry.Message);
        Assert.Contains("HttpStatus=200", entry.Message);
        Assert.Contains("ContentType=text/html", entry.Message);
        Assert.Contains("BodyLength=500", entry.Message);
        Assert.Contains("BodyStart=" + new string('A', 150) + new string('B', 50), entry.Message);
        Assert.DoesNotContain(new string('B', 51), entry.Message);
    }

    [Fact]
    public async Task EvaluateAsync_When2xxBodyHasLineBreaks_LogsSnippetOnOneLine()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "dòng một\r\ndòng hai\ndòng ba", "text/plain"));

        await Evaluate(sut);

        var entry = Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.DoesNotContain((char)10, entry.Message);
        Assert.DoesNotContain((char)13, entry.Message);
        Assert.Contains("dòng một", entry.Message);
        Assert.Contains("dòng ba", entry.Message);
    }

    [Fact]
    public async Task GetDecisionAsync_When2xxBodyIsUnusable_ReturnsInvalidResponseAndLogsDashForKey()
    {
        var sut = CreateReplying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "<html/>", "text/html"));

        var result = await sut.Client.GetDecisionAsync(TadssTestData.DecisionId);

        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        Assert.Contains("IdempotencyKey=-", Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error).Message);
    }

    // ── Log không lộ bí mật ──────────────────────────────────────────────────────

    [Fact]
    public async Task AllOperations_WhenSucceedingOrFailing_NeverLogApiKeyOrRequestBody()
    {
        var sut = Create(new FakeHttpMessageHandler((n, _) => Task.FromResult(n switch
        {
            1 => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson),
            2 => FakeHttpMessageHandler.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"),
            3 => FakeHttpMessageHandler.Json(HttpStatusCode.OK, "<html>nope</html>", "text/html"),
            _ => FakeHttpMessageHandler.Problem(HttpStatusCode.InternalServerError, "INTERNAL_ERROR")
        })));

        await Evaluate(sut);
        await Evaluate(sut);
        await Evaluate(sut);
        await Evaluate(sut); // 5xx: có log thử lại

        Assert.NotEmpty(sut.Logger.Entries);
        Assert.All(sut.Logger.Entries, e =>
        {
            Assert.DoesNotContain(ApiKey, e.FullText);
            Assert.DoesNotContain("promotionCode", e.FullText);
            Assert.DoesNotContain("WINTER", e.FullText);
        });
    }

    [Fact]
    public async Task EvaluateAsync_WhenNetworkErrorIsLogged_DoesNotLeakApiKeyThroughException()
    {
        var sut = Create(new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused")));

        await Evaluate(sut);

        Assert.Contains(sut.Logger.Entries, e => e.Exception is HttpRequestException);
        Assert.All(sut.Logger.Entries, e => Assert.DoesNotContain(ApiKey, e.FullText));
    }
}
