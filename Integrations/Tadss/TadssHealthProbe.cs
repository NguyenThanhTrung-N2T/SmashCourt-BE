using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Kiểm <c>/health/ready</c> của TADSS (P6-57) với timeout <c>CircuitProbeTimeoutMs</c>. Endpoint này miễn API key và kiểm PostgreSQL
/// (và Redis khi TADSS dùng Redis) của TADSS. Kết quả chỉ dùng để mở mạch nhanh và cho đơn thử sớm; mạch chỉ đóng khi đơn thử thành công.
/// </summary>
public sealed class TadssHealthProbe : ITadssHealthProbe
{
    private const string ReadyPath = "health/ready";

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<TadssSettings> _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<TadssHealthProbe> _logger;

    public TadssHealthProbe(
        HttpClient httpClient, IOptionsMonitor<TadssSettings> settings, TimeProvider time, ILogger<TadssHealthProbe> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        var settings = _settings.CurrentValue;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl)
            || !Uri.TryCreate(settings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            return false;

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(settings.CircuitProbeTimeoutMs), _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(baseUri, ReadyPath), linked.Token);
            if (!response.IsSuccessStatusCode)
                _logger.LogInformation("Kiểm /health/ready của TADSS: HTTP {Status}", (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Kiểm /health/ready của TADSS quá {TimeoutMs} ms", settings.CircuitProbeTimeoutMs);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            _logger.LogInformation("Kiểm /health/ready của TADSS lỗi mạng ({ExceptionType})", ex.GetType().Name);
            return false;
        }
    }
}
