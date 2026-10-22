using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss.DTOs;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// HTTP client gọi TADSS Decision Runtime v2. Không dùng Polly: chỉ <see cref="EvaluateAsync"/> thử lại đúng một lần
/// bằng vòng lặp tường minh để bảo đảm cùng idempotency key và không thử lại lỗi 4xx.
/// Cấu hình (BaseUrl, ApiKey, TimeoutMs) đọc từ <see cref="IOptionsMonitor{TOptions}"/> ở mỗi lần gọi.
/// Không bao giờ ghi khóa API hay body request vào log.
/// </summary>
public class TadssClient : ITadssClient
{
    private const int LogBodySnippetLength = 200;
    private const string DecisionsPath = "api/v2/decisions";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<TadssSettings> _settings;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TadssClient> _logger;

    public TadssClient(
        HttpClient httpClient,
        IOptionsMonitor<TadssSettings> settings,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ILogger<TadssClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(
        TadssDecisionRequestDto request, Guid bookingId, CancellationToken cancellationToken = default)
    {
        var call = new TadssCall("Evaluate", HttpMethod.Post, DecisionsPath, request, bookingId.ToString(), null);
        return SendAsync<TadssDecisionResultDto>(call, allowRetry: true, CheckDecisionResult, cancellationToken);
    }

    public Task<TadssCallResult<TadssOverrideResponseDto>> OverrideAsync(
        Guid decisionId, TadssOverrideRequestDto request, string actorId, CancellationToken cancellationToken = default)
    {
        var call = new TadssCall("Override", HttpMethod.Post, $"{DecisionsPath}/{decisionId}/override", request,
            $"{decisionId}:{request.Action}", actorId);
        return SendAsync<TadssOverrideResponseDto>(call, allowRetry: false, null, cancellationToken);
    }

    public Task<TadssCallResult<TadssDecisionDetailDto>> GetDecisionAsync(
        Guid decisionId, CancellationToken cancellationToken = default)
    {
        var call = new TadssCall("GetDecision", HttpMethod.Get, $"{DecisionsPath}/{decisionId}", null, null, null);
        return SendAsync<TadssDecisionDetailDto>(call, allowRetry: false, null, cancellationToken);
    }

    public Task<TadssCallResult<TadssOutcomeResponseDto>> ReportOutcomeAsync(
        Guid decisionId, TadssOutcomeRequestDto request, CancellationToken cancellationToken = default)
    {
        var call = new TadssCall("ReportOutcome", HttpMethod.Post, $"{DecisionsPath}/{decisionId}/outcome", request,
            $"{decisionId}:OUTCOME", null);
        return SendAsync<TadssOutcomeResponseDto>(call, allowRetry: false, null, cancellationToken);
    }

    // INV-7 của contract: APPROVED ⇔ deposit, NEED_APPROVAL ⇔ approvalExpiresAt; cùng phạm vi deposit của decision-result.schema.json
    // (rate ∈ [0, 1], amount ≥ 0). Lệch thì không dùng được kết quả: ghi vào hóa đơn sẽ bị CHECK của DB chặn ở transaction 2 (F1)
    private static string? CheckDecisionResult(TadssDecisionResultDto result) => result.Status switch
    {
        TadssDecisionStatus.APPROVED when result.Deposit is null => "status APPROVED nhưng thiếu deposit",
        TadssDecisionStatus.NEED_APPROVAL when result.ApprovalExpiresAt is null => "status NEED_APPROVAL nhưng thiếu approvalExpiresAt",
        _ when result.Deposit is { Rate: < 0m or > 1m } => "deposit.rate ngoài [0, 1]",
        _ when result.Deposit is { Amount: < 0m } => "deposit.amount âm",
        _ => null
    };

    private async Task<TadssCallResult<T>> SendAsync<T>(
        TadssCall call, bool allowRetry, Func<T, string?>? check, CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        var correlationId = ResolveCorrelationId();

        if (!TryBuildUri(settings.BaseUrl, call.Path, out var uri))
        {
            _logger.LogError("TADSS {Operation}: BaseUrl không hợp lệ. CorrelationId={CorrelationId}", call.Operation, correlationId);
            return TadssCallResult<T>.Failure(TadssFailureKind.Network, correlationId);
        }

        var maxAttempts = allowRetry ? 2 : 1;
        for (var attempt = 1; ; attempt++)
        {
            var result = await SendOnceAsync(call, uri, settings, correlationId, check, cancellationToken);
            if (result.IsSuccess || attempt >= maxAttempts || !IsRetryable(result))
                return result;

            _logger.LogWarning(
                "TADSS {Operation} lỗi tạm thời ({Kind}, HTTP {Status}), thử lại một lần với cùng idempotency key. CorrelationId={CorrelationId}",
                call.Operation, result.FailureKind, result.HttpStatus, correlationId);
        }
    }

    private async Task<TadssCallResult<T>> SendOnceAsync<T>(
        TadssCall call, Uri uri, TadssSettings settings, string correlationId, Func<T, string?>? check,
        CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetTimestamp();
        // Timeout tính riêng cho từng lần gọi; token của caller hủy thì phân biệt được với timeout
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(settings.TimeoutMs), _timeProvider);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var request = BuildRequest(call, uri, settings.ApiKey, correlationId);
            using var response = await _httpClient.SendAsync(request, linkedSource.Token);
            var body = await response.Content.ReadAsStringAsync(linkedSource.Token);
            var elapsedMs = (long)_timeProvider.GetElapsedTime(started).TotalMilliseconds;

            return response.IsSuccessStatusCode
                ? ReadSuccess(call, response, body, elapsedMs, correlationId, check)
                : ReadFailure<T>(call, response, body, elapsedMs, correlationId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "TADSS {Operation} quá thời gian chờ {TimeoutMs} ms. CorrelationId={CorrelationId}",
                call.Operation, settings.TimeoutMs, correlationId);
            return TadssCallResult<T>.Failure(TadssFailureKind.Timeout, correlationId);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogWarning(ex, "TADSS {Operation} lỗi mạng. CorrelationId={CorrelationId}", call.Operation, correlationId);
            return TadssCallResult<T>.Failure(TadssFailureKind.Network, correlationId);
        }
    }

    private TadssCallResult<T> ReadSuccess<T>(
        TadssCall call, HttpResponseMessage response, string body, long elapsedMs, string correlationId, Func<T, string?>? check)
    {
        var status = (int)response.StatusCode;
        string? problem = null;
        T? value = default;
        try
        {
            value = JsonSerializer.Deserialize<T>(body, TadssJson.Options);
            if (value is null)
                problem = "body rỗng hoặc null";
            else
                problem = check?.Invoke(value);
        }
        catch (JsonException ex)
        {
            problem = $"JSON không đọc được ({ex.GetType().Name})";
        }

        if (problem != null || value is null)
        {
            LogInvalidResponse(call, response, body, problem, correlationId);
            return TadssCallResult<T>.Failure(TadssFailureKind.InvalidResponse, correlationId, status);
        }

        _logger.LogInformation(
            "TADSS {Operation} thành công HTTP {Status} trong {ElapsedMs} ms. CorrelationId={CorrelationId}",
            call.Operation, status, elapsedMs, correlationId);
        return TadssCallResult<T>.Success(value, status, correlationId);
    }

    private TadssCallResult<T> ReadFailure<T>(
        TadssCall call, HttpResponseMessage response, string body, long elapsedMs, string correlationId)
    {
        var status = (int)response.StatusCode;
        var problem = TryReadProblem(body);
        var retryAfter = ReadRetryAfter(response.Headers.RetryAfter, problem);

        _logger.LogWarning(
            "TADSS {Operation} trả HTTP {Status} code={Code} sau {ElapsedMs} ms. CorrelationId={CorrelationId}",
            call.Operation, status, problem?.Code, elapsedMs, correlationId);
        return TadssCallResult<T>.Failure(TadssFailureKind.Http, correlationId, status, problem?.Code, retryAfter);
    }

    private void LogInvalidResponse(TadssCall call, HttpResponseMessage response, string body, string? problem, string correlationId)
    {
        var snippet = body.Length <= LogBodySnippetLength ? body : body[..LogBodySnippetLength];
        snippet = snippet.Replace('\r', ' ').Replace('\n', ' ');

        _logger.LogError(
            "TADSS {Operation} trả 2xx nhưng phản hồi không dùng được ({Problem}). CorrelationId={CorrelationId}, IdempotencyKey={IdempotencyKey}, " +
            "HttpStatus={Status}, ContentType={ContentType}, BodyLength={BodyLength}, BodyStart={BodyStart}",
            call.Operation, problem, correlationId, call.IdempotencyKey ?? "-", (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? "-", body.Length, snippet);
    }

    private static HttpRequestMessage BuildRequest(TadssCall call, Uri uri, string apiKey, string correlationId)
    {
        var request = new HttpRequestMessage(call.Method, uri);
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        // Bỏ qua trang cảnh báo của ngrok (giống FastApiClient)
        request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        if (call.IdempotencyKey != null)
            request.Headers.TryAddWithoutValidation("X-Idempotency-Key", call.IdempotencyKey);
        if (call.ActorId != null)
            request.Headers.TryAddWithoutValidation("X-Actor-Id", call.ActorId);

        if (call.Body != null)
        {
            var json = JsonSerializer.Serialize(call.Body, call.Body.GetType(), TadssJson.Options);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    // Retry theo INTEGRATION_SPEC 4.3: timeout, lỗi mạng, 5xx. Không retry 4xx (kể cả 413, 429) và InvalidResponse
    private static bool IsRetryable<T>(TadssCallResult<T> result) => result.FailureKind switch
    {
        TadssFailureKind.Timeout or TadssFailureKind.Network => true,
        TadssFailureKind.Http => result.HttpStatus >= 500,
        _ => false
    };

    private TadssProblemDto? TryReadProblem(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TadssProblemDto>(body, TadssJson.Options);
        }
        catch (JsonException)
        {
            return null; // Không phải problem+json (proxy, ngrok...): phân loại theo status
        }
    }

    private TimeSpan? ReadRetryAfter(RetryConditionHeaderValue? header, TadssProblemDto? problem)
    {
        if (header?.Delta is { } delta)
            return delta;
        if (header?.Date is { } date)
        {
            var wait = date - _timeProvider.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return problem?.RetryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
    }

    // Dùng lại id của request đang xử lý để log BE và TADSS khớp nhau; ngoài request (job) thì sinh mới
    private string ResolveCorrelationId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context != null)
        {
            var incoming = context.Request.Headers["X-Correlation-ID"].ToString();
            if (IsUsableCorrelationId(incoming))
                return incoming;
            if (IsUsableCorrelationId(context.TraceIdentifier))
                return context.TraceIdentifier;
        }

        return Guid.NewGuid().ToString("N");
    }

    // Giá trị do FE gửi lên nên không tin: ký tự ngoài ASCII hiển thị làm HttpClient từ chối cả request (thành lỗi mạng giả)
    private static bool IsUsableCorrelationId(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 128 && value.All(c => c is > ' ' and <= '~');

    private static bool TryBuildUri(string baseUrl, string path, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return false;
        return Uri.TryCreate(baseUri, path, out uri!);
    }

    private sealed record TadssCall(
        string Operation, HttpMethod Method, string Path, object? Body, string? IdempotencyKey, string? ActorId);
}
