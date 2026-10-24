using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-2c Task 4: mọi sự cố TADSS khi đặt sân online ⇒ cọc 100 % (d), hoặc a′ khi đơn dưới mức tối thiểu; khách không thấy lỗi TADSS
/// (BE-IT-04, Z-13…Z-15). Số lần gọi theo INTEGRATION_SPEC 4.3: thử lại một lần khi timeout, lỗi mạng, 5xx; không thử lại 4xx.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Tadss")]
public sealed class OnlineBookingTadssFailureTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : OnlineBookingTestBase(database, host)
{
    private const string ProblemCode = "PERSISTENCE_UNAVAILABLE";

    private static string Problem(int status) =>
        $$"""{"type":"about:blank","title":"Lỗi","status":{{status}},"code":"{{ProblemCode}}","correlationId":"c-1"}""";

    /// <summary>Đặt một đơn 200.000 đ khi TADSS lỗi; kiểm response là nhánh d bình thường, không lộ lỗi TADSS.</summary>
    private async Task<BookingCall> BookExpectingFullPaymentAsync(BookableCourt court)
    {
        var (call, _) = await BookAsync(court);

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("Đặt sân online thành công", call.Message);
        Assert.Equal("PENDING", call.DataString("status"));
        Assert.Equal("FULL_PAYMENT_FALLBACK", call.DataString("depositSource"));
        Assert.Equal((1m, 200_000m, 0m),
            (call.DataDecimal("depositRate"), call.DataDecimal("depositAmount"), call.DataDecimal("remainingAmount")));
        Assert.Equal(200_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));
        Assert.DoesNotContain(ProblemCode, call.Envelope.GetRawText());
        Assert.DoesNotContain("TADSS", call.Message ?? "");
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Null(booking.TadssDecisionId);
        Assert.Equal(200_000m, Assert.Single(booking.Invoice?.Payments ?? []).Amount);
        return call;
    }

    private static void AssertSameKeyAndBody(IReadOnlyList<WireMock.Logging.ILogEntry> requests, Guid bookingId)
    {
        Assert.All(requests, r => Assert.Equal(bookingId.ToString(), r.RequestMessage?.Headers?["X-Idempotency-Key"].Single()));
        Assert.Single(requests.Select(r => r.RequestMessage?.Body).Distinct());
    }

    // ───────────────────────── Thử lại một lần ─────────────────────────

    [Theory]
    [Trait("Catalog", "BE-IT-04")]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task ServerError_IsRetriedOnceWithTheSameKeyAndBody_ThenFullPayment(int status)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.Reply(status, Problem(status));

        var call = await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));

        var requests = await tadss.WaitForEvaluationsAsync(call.BookingId, 2);
        Assert.Equal(2, requests.Count);
        AssertSameKeyAndBody(requests, call.BookingId);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task NetworkError_IsRetriedOnce_ThenFullPayment()
    {
        // Lỗi mạng thật: một cổng TCP nhận kết nối rồi cắt ngay (RST). Fault của WireMock.Net không cắt được kết nối (EMPTY_RESPONSE trả
        // 200 body rỗng, ca riêng bên dưới); cổng đóng hẳn thì Windows chờ ~2 s mới báo từ chối nên thành timeout.
        using var _ = await UseTadssAsync();
        var factory = await GetFactoryAsync();
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var connections = 0;
        using var stop = new CancellationTokenSource();
        var acceptLoop = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var socket = await listener.AcceptSocketAsync(stop.Token);
                Interlocked.Increment(ref connections);
                socket.LingerState = new System.Net.Sockets.LingerOption(true, 0); // đóng bằng RST
                socket.Close();
            }
        });
        factory.SetSetting("TadssConfig:BaseUrl", $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        var court = await SeedCourtAsync(200_000m);
        factory.Logs.Clear();
        try
        {
            var call = await BookExpectingFullPaymentAsync(court);

            Assert.Equal(2, connections);
            Assert.Equal(2, factory.Logs.Entries.Count(e =>
                e.Category.EndsWith("TadssClient", StringComparison.Ordinal) && e.Message.Contains("lỗi mạng")));
            Assert.Contains(factory.Logs.Entries, e => e.Message.Contains(call.BookingId.ToString()) && e.Message.Contains("Network"));
        }
        finally
        {
            factory.SetSetting("TadssConfig:BaseUrl", null);
            stop.Cancel();
            listener.Stop();
            await Task.WhenAny(acceptLoop, Task.Delay(1_000));
        }
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task EmptyResponse_IsAnInvalidResponse_NotRetried_ThenFullPayment()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.FailWithNetworkFault();

        var call = await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));

        Assert.Single(await tadss.WaitForEvaluationsAsync(call.BookingId, 1));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task Timeout_WaitsTwiceTheTimeout_ThenFullPayment()
    {
        // WireMock không ghi log request bị cắt giữa chừng: kiểm bằng thời gian (hai lần chờ TimeoutMs)
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync(); // ca kiểm timeout: giữ 400 ms của host
        tadss.ReplyTooSlowly();
        var court = await SeedCourtAsync(200_000m);

        var watch = Stopwatch.StartNew();
        await BookExpectingFullPaymentAsync(court);
        watch.Stop();

        Assert.InRange(watch.ElapsedMilliseconds, 2 * SmashCourtWebApplicationFactory.TadssTimeoutMs - 100, 3_000);
    }

    // ───────────────────────── Không thử lại ─────────────────────────

    [Theory]
    [Trait("Catalog", "BE-IT-04")]
    [InlineData(401, null)]
    [InlineData(413, null)]
    [InlineData(429, "30")]
    public async Task ClientErrorsOfTheInfrastructure_AreNotRetried_ThenFullPayment(int status, string? retryAfter)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.Reply(status, Problem(status), retryAfter: retryAfter);

        var call = await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));

        Assert.Single(await tadss.WaitForEvaluationsAsync(call.BookingId, 1));
        await Task.Delay(300);
        Assert.Single(tadss.EvaluationsFor(call.BookingId));
    }

    [Theory]
    [Trait("Catalog", "BE-IT-04")]
    [InlineData(400)]
    [InlineData(409)]
    public async Task UnexpectedBadRequestOrConflict_IsLoggedAsError_ThenFullPayment(int status)
    {
        // R-4: TADSS trả 400/409 nghĩa là BE dựng request sai ⇒ lỗi lập trình, log Error; khách vẫn đặt được
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.Reply(status, Problem(status));
        var factory = await GetFactoryAsync();

        var call = await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));

        Assert.Single(await tadss.WaitForEvaluationsAsync(call.BookingId, 1));
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(call.BookingId.ToString()));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task MalformedResponse_IsNotRetried_ThenFullPayment()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.Reply(200, """{"status":"APPROVED"}""", "application/json");

        var call = await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));

        Assert.Single(await tadss.WaitForEvaluationsAsync(call.BookingId, 1));
    }

    [Theory]
    [InlineData("decision-result.approved.json")]
    [InlineData("decision-result.need-approval.json")]
    [InlineData("decision-result.rejected.json")]
    public async Task DecisionForAnotherBooking_IsNotApplied_ThenFullPayment(string sample)
    {
        // TADSS trả nhầm quyết định của booking khác (chốt 24/10): không hủy oan, không chờ duyệt theo lý do của người khác, không lưu decisionId
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(); // TADSS có trả lời: không được để timeout chen vào
        tadss.ReplyWithSample(sample, Guid.NewGuid(), json =>
        {
            if (json["approvalExpiresAt"] is not null)
                json["approvalExpiresAt"] = DateTimeOffset.UtcNow.AddMinutes(20).ToString("O");
        });
        var factory = await GetFactoryAsync();

        var call = await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));

        Assert.Single(await tadss.WaitForEvaluationsAsync(call.BookingId, 1));
        Assert.Contains(factory.Logs.Entries, e =>
            e.Level == LogLevel.Error && e.Message.Contains("reservationId") && e.Message.Contains(call.BookingId.ToString()));
    }

    [Fact]
    public async Task ApprovedDepositAboveTheOrderTotal_IsIgnored_ThenFullPayment()
    {
        // F1: quyết định không khớp đơn ⇒ không thu quá tổng đơn
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        tadss.ApproveAnyBooking(1m, 250_000m);

        await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));
    }

    // ───────────────────────── Z-13…Z-15 ─────────────────────────

    [Fact]
    [Trait("Catalog", "Z-13")]
    public async Task FirstAttemptTimesOut_SecondAnswers_TheSecondDecisionIsApplied()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync(); // ca kiểm timeout: giữ 400 ms của host
        tadss.ReplyTooSlowlyOnceThen("decision-result.approved.json",
            TimeSpan.FromMilliseconds(SmashCourtWebApplicationFactory.TadssTimeoutMs + 300),
            json => json["deposit"] = new System.Text.Json.Nodes.JsonObject { ["rate"] = 0.5m, ["amount"] = 100_000m, ["currency"] = "VND" });

        var (call, _) = await BookAsync(await SeedCourtAsync(200_000m));

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("TADSS", call.DataString("depositSource"));
        Assert.Equal(100_000m, VnPayAmount(call.DataString("paymentUrl") ?? ""));
        Assert.NotNull((await RequireBookingAsync(call.BookingId)).TadssDecisionId);
        var requests = await tadss.WaitForEvaluationsAsync(call.BookingId, 2);
        Assert.Equal(2, requests.Count);
        AssertSameKeyAndBody(requests, call.BookingId);
    }

    [Fact]
    [Trait("Catalog", "Z-14")]
    public async Task BothAttemptsTimeOut_LargeOrder_IsFullPaymentWithoutDecision()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync(); // ca kiểm timeout: giữ 400 ms của host
        tadss.ReplyTooSlowly();

        await BookExpectingFullPaymentAsync(await SeedCourtAsync(200_000m));
    }

    [Fact]
    [Trait("Catalog", "Z-15")]
    public async Task BothAttemptsTimeOut_SmallOrder_IsConfirmedAsFallbackWithAWarning()
    {
        // Ngoại lệ có chủ đích: 100 % của đơn < 5.000 đ không thu online được ⇒ a′ nguồn FULL_PAYMENT_FALLBACK, không trả lỗi
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync(); // ca kiểm timeout: giữ 400 ms của host
        tadss.ReplyTooSlowly();
        var factory = await GetFactoryAsync();

        var (call, _) = await BookAsync(await SeedCourtAsync(3_000m));

        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("CONFIRMED", call.DataString("status"));
        Assert.Equal("FULL_PAYMENT_FALLBACK", call.DataString("depositSource"));
        Assert.Equal((0m, 0m, 3_000m), (call.DataDecimal("depositRate"), call.DataDecimal("depositAmount"), call.DataDecimal("remainingAmount")));
        var booking = await RequireBookingAsync(call.BookingId);
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        Assert.Null(booking.TadssDecisionId);
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(call.BookingId.ToString()));
    }
}
