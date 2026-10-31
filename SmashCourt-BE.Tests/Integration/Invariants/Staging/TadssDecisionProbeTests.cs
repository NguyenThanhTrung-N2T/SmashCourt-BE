using System.Net;
using System.Net.Sockets;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>
/// SC6-5 (plan 6A-6 Task 11): phân loại phản hồi của <c>GET /api/v2/decisions/{id}</c> trên TADSS giả (WireMock). Chỉ 404 problem+json
/// <c>DECISION_NOT_FOUND</c> là vi phạm; mọi lỗi khác không bao giờ được tính là đạt; tối đa 8 request đồng thời.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-20")]
public sealed class TadssDecisionProbeTests : IDisposable
{
    private readonly WireMockServer _tadss = WireMockServer.Start();

    public void Dispose() => _tadss.Stop();

    private HttpClient Client(HttpMessageHandler? handler = null) =>
        new(handler ?? new HttpClientHandler()) { BaseAddress = new Uri(_tadss.Url! + "/") };

    private void Reply(Guid decisionId, int status, string? body = null, string? contentType = null, TimeSpan? delay = null)
    {
        var response = Response.Create().WithStatusCode(status);
        if (body != null)
            response = response.WithBody(body);
        if (contentType != null)
            response = response.WithHeader("Content-Type", contentType);
        if (delay != null)
            response = response.WithDelay(delay.Value);
        _tadss.Given(Request.Create().WithPath($"/api/v2/decisions/{decisionId}").UsingGet()).RespondWith(response);
    }

    private static string Problem(string code) =>
        $$"""{"type":"https://tadss/errors/{{code}}","title":"x","status":404,"code":"{{code}}"}""";

    // Ca kiểm phân loại dùng timeout rộng: kết quả không được phụ thuộc tốc độ máy (lần chạy đầu khi máy bận từng vượt 5 giây);
    // chỉ ca kiểm timeout đặt ngắn có chủ đích
    private static readonly TimeSpan ClassificationTimeout = TimeSpan.FromSeconds(30);

    private async Task<DecisionCheck> CheckOneAsync(Guid decisionId, TimeSpan? timeout = null)
    {
        using var client = Client();
        return Assert.Single(await TadssDecisionProbe.CheckAsync(client, [(Guid.NewGuid(), decisionId)], timeout: timeout ?? ClassificationTimeout));
    }

    [Fact]
    public async Task Ok_WithTheSameDecisionId_IsFound()
    {
        var id = Guid.NewGuid();
        Reply(id, 200, $$"""{"decisionId":"{{id}}","reservationId":"{{Guid.NewGuid()}}"}""", "application/json");

        var check = await CheckOneAsync(id);

        Assert.Equal(DecisionCheckOutcome.Found, check.Outcome);
        Assert.Equal(id, check.DecisionId);
    }

    [Theory]
    [InlineData("""{"decisionId":"00000000-0000-0000-0000-000000000001"}""")] // id khác
    [InlineData("""{"reservationId":"x"}""")]                                   // thiếu decisionId
    [InlineData("<html>ok</html>")]                                            // không phải JSON
    [InlineData("[]")]
    public async Task Ok_WithoutTheRightDecisionId_IsInconclusive(string body)
    {
        var id = Guid.NewGuid();
        Reply(id, 200, body, "application/json");

        Assert.Equal(DecisionCheckOutcome.Inconclusive, (await CheckOneAsync(id)).Outcome);
    }

    [Theory]
    [InlineData("application/problem+json")]
    [InlineData("application/problem+json; charset=utf-8")]
    public async Task NotFound_ProblemJsonDecisionNotFound_IsMissing(string contentType)
    {
        var id = Guid.NewGuid();
        Reply(id, 404, Problem("DECISION_NOT_FOUND"), contentType);

        var check = await CheckOneAsync(id);

        Assert.Equal(DecisionCheckOutcome.Missing, check.Outcome);
        Assert.Contains("DECISION_NOT_FOUND", check.Reason);
    }

    [Theory]
    [InlineData(null, null)]                                                          // 404 thân trống (proxy, route sai)
    [InlineData("<html>Not Found</html>", "text/html")]
    [InlineData("""{"code":"DECISION_NOT_FOUND"}""", "application/json")]             // đúng mã nhưng sai media type
    [InlineData("""{"code":"VALIDATION_ERROR"}""", "application/problem+json")]       // problem+json mã khác
    public async Task NotFound_NotTheContractShape_IsInconclusive(string? body, string? contentType)
    {
        var id = Guid.NewGuid();
        Reply(id, 404, body, contentType);

        var check = await CheckOneAsync(id);

        Assert.Equal(DecisionCheckOutcome.Inconclusive, check.Outcome);
        Assert.StartsWith("404", check.Reason);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task OtherStatuses_AreInconclusive(int status)
    {
        var id = Guid.NewGuid();
        Reply(id, status, Problem("X"), "application/problem+json");

        var check = await CheckOneAsync(id);

        Assert.Equal(DecisionCheckOutcome.Inconclusive, check.Outcome);
        Assert.Equal($"HTTP {status}", check.Reason);
    }

    [Fact]
    public async Task SlowerThanTheTimeout_IsInconclusive()
    {
        var id = Guid.NewGuid();
        Reply(id, 200, $$"""{"decisionId":"{{id}}"}""", "application/json", delay: TimeSpan.FromSeconds(2));

        var check = await CheckOneAsync(id, timeout: TimeSpan.FromMilliseconds(300));

        Assert.Equal(DecisionCheckOutcome.Inconclusive, check.Outcome);
        Assert.Contains("không phản hồi", check.Reason);
    }

    [Fact]
    public void DefaultLimits_AreFiveSecondsAndEightRequests()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), TadssDecisionProbe.DefaultTimeout);
        Assert.Equal(8, TadssDecisionProbe.DefaultMaxConcurrency);
    }

    [Fact]
    public async Task NetworkError_IsInconclusive()
    {
        // Cổng vừa cấp rồi đóng: kết nối bị từ chối
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

        var check = Assert.Single(await TadssDecisionProbe.CheckAsync(client, [(Guid.NewGuid(), Guid.NewGuid())]));

        Assert.Equal(DecisionCheckOutcome.Inconclusive, check.Outcome);
        Assert.StartsWith("lỗi mạng", check.Reason);
    }

    [Fact]
    public async Task ThirtyDecisions_AtMostEightInFlight_EveryResultMatchesItsBooking()
    {
        var items = Enumerable.Range(0, 30).Select(_ => (BookingId: Guid.NewGuid(), DecisionId: Guid.NewGuid())).ToList();
        foreach (var (_, decisionId) in items)
            Reply(decisionId, 200, $$"""{"decisionId":"{{decisionId}}"}""", "application/json", delay: TimeSpan.FromMilliseconds(150));
        var counter = new InFlightCounter(new HttpClientHandler());
        using var client = Client(counter);

        var checks = await TadssDecisionProbe.CheckAsync(client, items, timeout: ClassificationTimeout);

        Assert.Equal(30, checks.Count);
        Assert.All(checks, c => Assert.Equal(DecisionCheckOutcome.Found, c.Outcome));
        Assert.Equal(items.ToHashSet(), checks.Select(c => (c.BookingId, c.DecisionId)).ToHashSet());
        Assert.InRange(counter.MaxInFlight, 2, 8); // có chạy song song, không vượt giới hạn
        Assert.Equal(30, counter.Total);
    }

    /// <summary>Đếm số request đang bay cùng lúc (đo ở phía client, ngay trước WireMock).</summary>
    private sealed class InFlightCounter(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private int _current;
        private int _max;
        private int _total;

        public int MaxInFlight => _max;
        public int Total => _total;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _current);
            Interlocked.Increment(ref _total);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }

            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }
}
