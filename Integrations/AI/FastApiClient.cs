using System.Net;
using System.Text;
using System.Text.Json;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace SmashCourt_BE.Integrations.AI;

/// <summary>
/// HTTP client dùng để giao tiếp với AI FastAPI.
/// Có retry, timeout và circuit breaker bằng Polly để tránh lỗi AI service làm hỏng API chính.
/// </summary>
public class FastApiClient : IFastApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FastApiClient> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public FastApiClient(HttpClient httpClient, ILogger<FastApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _resiliencePipeline = CreateResiliencePipeline();
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken = default)
    {
        return await SendWithRetryAsync<TResponse>(async token =>
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync(path, content, token);
        }, path, cancellationToken);
    }

    public async Task<TResponse?> GetAsync<TResponse>(string path, CancellationToken cancellationToken = default)
    {
        return await SendWithRetryAsync<TResponse>(
            token => _httpClient.GetAsync(path, token),
            path,
            cancellationToken);
    }

    private async Task<TResponse?> SendWithRetryAsync<TResponse>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _resiliencePipeline.ExecuteAsync(
                async token => await send(token),
                cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<TResponse>(responseBody, _jsonOptions);
            }

            _logger.LogWarning(
                "AI Service request failed. Path={Path}, Status={StatusCode}, Body={Body}",
                path,
                response.StatusCode,
                responseBody);

            return default;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize AI Service response. Path={Path}", path);
            return default;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "AI Service circuit breaker is open. Path={Path}", path);
            return default;
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning(ex, "AI Service request timed out. Path={Path}", path);
            return default;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "AI Service request timed out. Path={Path}", path);
            return default;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AI Service request error. Path={Path}", path);
            return default;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.TooManyRequests ||
               (int)statusCode >= 500;
    }

    private static ResiliencePipeline<HttpResponseMessage> CreateResiliencePipeline()
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => IsTransient(response.StatusCode))
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 1.0,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(response => IsTransient(response.StatusCode))
            })
            .AddTimeout(TimeSpan.FromSeconds(60))
            .Build();
    }
}
