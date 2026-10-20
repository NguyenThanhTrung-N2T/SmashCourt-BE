using System.Net;
using System.Text;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>Một request đã đi qua <see cref="FakeHttpMessageHandler"/> (header và body được chụp lại vì request bị dispose sau khi gửi).</summary>
internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    string? ContentType,
    CancellationToken Token)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// <see cref="HttpMessageHandler"/> giả: chụp lại mọi request và để test quyết định response theo số thứ tự lần gọi (từ 1).
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<int, CapturedRequest, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = [];

    public IReadOnlyList<CapturedRequest> Requests => _requests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedRequest(
            request.Method, request.RequestUri!, headers, body, request.Content?.Headers.ContentType?.MediaType, cancellationToken);
        _requests.Add(captured);
        return await respond(_requests.Count, captured);
    }

    public static FakeHttpMessageHandler Always(Func<HttpResponseMessage> response) =>
        new((_, _) => Task.FromResult(response()));

    public static HttpResponseMessage Json(HttpStatusCode status, string body, string contentType = "application/json") => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, contentType)
    };

    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string? retryAfterHeader = null)
    {
        var response = Json(status, $$"""{"title":"lỗi","status":{{(int)status}},"code":"{{code}}","traceId":"t-1"}""", "application/problem+json");
        if (retryAfterHeader != null)
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfterHeader);
        return response;
    }
}
