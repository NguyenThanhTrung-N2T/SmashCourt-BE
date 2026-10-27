using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// Gói 6A-5 Task 7: client kiểm response của outcome (B4) và dùng timeout riêng 5 giây (C8 — P6-26). Outcome là nhãn đánh giá: BE nhận 200
/// thì đánh dấu đã báo và không bao giờ gửi lại, nên 200 sai nội dung (proxy trỏ nhầm TADSS khác) phải thành <c>InvalidResponse</c>.
/// <c>occurredAt</c> so sau khi cắt về µs (TADSS lưu timestamptz, so trùng nội dung sau khi cắt µs — INTEGRATION_SPEC §3.7).
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-01")]
public class TadssClientOutcomeTests
{
    private sealed record Sut(TadssClient Client, FakeHttpMessageHandler Handler, ListLogger<TadssClient> Logger);

    private static readonly DateTimeOffset OccurredAt = new DateTimeOffset(2026, 11, 20, 18, 30, 0, TimeSpan.FromHours(7)).AddTicks(1_234_560);

    private static TadssOutcomeRequestDto Sent(DateTimeOffset? occurredAt = null) => new()
    {
        Outcome = TadssOutcome.NO_SHOW,
        OccurredAt = occurredAt ?? OccurredAt
    };

    private static Sut Create(FakeHttpMessageHandler handler, TimeProvider? time = null)
    {
        var settings = new TadssSettings { EnableTadssDecision = true, BaseUrl = "http://tadss.test:5296", ApiKey = "key", TimeoutMs = 800 };
        var monitor = new Mock<IOptionsMonitor<TadssSettings>>();
        monitor.Setup(x => x.CurrentValue).Returns(settings);
        var logger = new ListLogger<TadssClient>();
        var client = new TadssClient(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, monitor.Object, new HttpContextAccessor(),
            time ?? TimeProvider.System, logger);
        return new Sut(client, handler, logger);
    }

    private static Sut Replying(string body, string contentType = "application/json") =>
        Create(FakeHttpMessageHandler.Always(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body, contentType)));

    /// <summary>Mẫu outcome-response của contract, sửa các trường theo ca test.</summary>
    private static string Response(Guid? decisionId = null, string outcome = "NO_SHOW", string? occurredAt = null)
    {
        var json = JsonNode.Parse(TadssContractFiles.Sample("outcome-response.json"))!.AsObject();
        json["decisionId"] = (decisionId ?? TadssTestData.DecisionId).ToString();
        json["outcome"] = outcome;
        json["occurredAt"] = occurredAt ?? OccurredAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz");
        return json.ToJsonString();
    }

    private static Task<TadssCallResult<TadssOutcomeResponseDto>> Report(Sut sut, TadssOutcomeRequestDto? request = null) =>
        sut.Client.ReportOutcomeAsync(TadssTestData.DecisionId, request ?? Sent());

    private static void AssertInvalid(Sut sut, TadssCallResult<TadssOutcomeResponseDto> result, string problemFragment)
    {
        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        Assert.Equal(200, result.HttpStatus);
        Assert.Single(sut.Handler.Requests); // không thử lại
        var error = Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(problemFragment, error.Message);
    }

    // ── Kiểm response (B4) ────────────────────────────────────────────────

    [Fact]
    public async Task ReportOutcome_ResponseMatchesWhatWasSent_Succeeds()
    {
        var sut = Replying(Response());

        var result = await Report(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal(TadssTestData.DecisionId, result.Value.DecisionId);
    }

    [Fact]
    public async Task ReportOutcome_ResponseForAnotherDecision_IsInvalidResponse()
    {
        // VD đường hầm trỏ nhầm sang một TADSS khác có quyết định trùng định dạng
        var other = Guid.NewGuid();
        var sut = Replying(Response(decisionId: other));

        AssertInvalid(sut, await Report(sut), other.ToString());
    }

    [Fact]
    public async Task ReportOutcome_ResponseWithAnotherOutcome_IsInvalidResponse()
    {
        var sut = Replying(Response(outcome: "COMPLETED"));

        AssertInvalid(sut, await Report(sut), "COMPLETED");
    }

    [Fact]
    public async Task ReportOutcome_OccurredAtOneMicrosecondOff_IsInvalidResponse()
    {
        var sut = Replying(Response(occurredAt: OccurredAt.AddTicks(10).ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz")));

        AssertInvalid(sut, await Report(sut), "occurredAt");
    }

    [Fact]
    public async Task ReportOutcome_OccurredAtDiffersOnlyBelowAMicrosecond_Succeeds()
    {
        // .NET giữ tới 100 ns; TADSS lưu và trả về µs. Request mang thêm 700 ns vẫn là cùng nội dung
        var sut = Replying(Response());

        var result = await Report(sut, Sent(OccurredAt.AddTicks(7)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ReportOutcome_SameInstantInAnotherOffset_Succeeds()
    {
        var sut = Replying(Response(occurredAt: OccurredAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.ffffffzzz")));

        Assert.True((await Report(sut)).IsSuccess);
    }

    [Theory]
    [InlineData("<html><body>ngrok: tunnel not found</body></html>", "text/html")]
    [InlineData("", "application/json")]
    [InlineData("null", "application/json")]
    public async Task ReportOutcome_200WithUnusableBody_IsInvalidResponse(string body, string contentType)
    {
        var sut = Replying(body, contentType);

        var result = await Report(sut);

        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        Assert.Single(sut.Handler.Requests);
    }

    [Fact]
    public void ContractSample_StillMatchesTheOutcomeResponseSchema()
    {
        // CT-04: mẫu dùng để dựng response giả vẫn đúng schema sau khi sửa trường
        var evaluation = TadssContractFiles.Evaluate(TadssContractFiles.OutcomeResponse, Response());

        Assert.True(evaluation.IsValid);
    }

    // ── Timeout riêng 5 giây (C8) ─────────────────────────────────────────

    private static FakeHttpMessageHandler NeverRespond() => new(async (_, request) =>
    {
        await Task.Delay(Timeout.Infinite, request.Token);
        return new HttpResponseMessage(HttpStatusCode.OK);
    });

    private static async Task WaitForRequestAsync(Sut sut)
    {
        for (var i = 0; i < 200 && sut.Handler.Requests.Count == 0; i++)
            await Task.Delay(10);
        Assert.Single(sut.Handler.Requests);
    }

    [Fact]
    public async Task ReportOutcome_UsesFiveSecondsInsteadOfTimeoutMs()
    {
        var time = new FakeTimeProvider();
        var sut = Create(NeverRespond(), time);

        var call = Report(sut);
        await WaitForRequestAsync(sut);

        // TimeoutMs (800 ms) và gần 5 giây: lệnh gửi outcome vẫn đang chờ (DB Neon của TADSS có thể đang thức dậy)
        time.Advance(TimeSpan.FromMilliseconds(800));
        await Task.Delay(50);
        Assert.False(call.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(4199));
        await Task.Delay(50);
        Assert.False(call.IsCompleted);

        time.Advance(TimeSpan.FromMilliseconds(1));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(TadssFailureKind.Timeout, result.FailureKind);
        Assert.Single(sut.Handler.Requests); // không thử lại
        Assert.Contains(sut.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("5000"));
    }

    [Fact]
    public async Task EvaluateAsync_StillUsesTimeoutMs()
    {
        var time = new FakeTimeProvider();
        var sut = Create(NeverRespond(), time);

        var call = sut.Client.EvaluateAsync(TadssTestData.DecisionRequest(full: true), TadssTestData.BookingId);
        await WaitForRequestAsync(sut);

        time.Advance(TimeSpan.FromMilliseconds(800));
        for (var i = 0; i < 200 && sut.Handler.Requests.Count < 2; i++)
            await Task.Delay(10);

        // Lần đầu hết hạn đúng 800 ms nên client đã thử lại lần hai
        Assert.Equal(2, sut.Handler.Requests.Count);
        Assert.True(sut.Handler.Requests[0].Token.IsCancellationRequested);
        time.Advance(TimeSpan.FromMilliseconds(800));
        Assert.Equal(TadssFailureKind.Timeout, (await call.WaitAsync(TimeSpan.FromSeconds(10))).FailureKind);
    }
}
