using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// Phase 6A-3: client kiểm response của ghi đè và chi tiết quyết định (spec 7.3: <c>decisionId</c> trả về phải khớp quyết định đã gửi),
/// và giữ tên trường lỗi của TADSS để BE tách "Quản lý nhập sai tỷ lệ" (400) với "BE gửi sai" (500).
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-04")]
public class TadssClientOverrideTests
{
    private sealed record Sut(TadssClient Client, FakeHttpMessageHandler Handler, ListLogger<TadssClient> Logger);

    private static Sut Replying(Func<HttpResponseMessage> response)
    {
        var settings = new TadssSettings { EnableTadssDecision = true, BaseUrl = "http://tadss.test:5296", ApiKey = "key", TimeoutMs = 800 };
        var monitor = new Mock<IOptionsMonitor<TadssSettings>>();
        monitor.Setup(x => x.CurrentValue).Returns(settings);
        var handler = FakeHttpMessageHandler.Always(response);
        var logger = new ListLogger<TadssClient>();
        var client = new TadssClient(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, monitor.Object, new HttpContextAccessor(), TimeProvider.System, logger);
        return new Sut(client, handler, logger);
    }

    private static string Sample(string file, Action<JsonObject>? customize = null)
    {
        var json = JsonNode.Parse(TadssContractFiles.Sample(file))!.AsObject();
        customize?.Invoke(json);
        return json.ToJsonString();
    }

    private static Sut ReplyingJson(string body) => Replying(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, body));

    private static Task<TadssCallResult<TadssOverrideResponseDto>> Approve(Sut sut) =>
        sut.Client.OverrideAsync(TadssTestData.DecisionId, TadssTestData.OverrideApprove(), "actor");

    private static Task<TadssCallResult<TadssOverrideResponseDto>> Reject(Sut sut) =>
        sut.Client.OverrideAsync(TadssTestData.DecisionId, TadssTestData.OverrideReject(), "actor");

    private static void AssertInvalid<T>(Sut sut, TadssCallResult<T> result, string problemFragment)
    {
        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        Assert.Equal(200, result.HttpStatus);
        Assert.Single(sut.Handler.Requests); // không thử lại
        var error = Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(problemFragment, error.Message);
    }

    // ── Ghi đè hợp lệ ──────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_MatchingResponse_Succeeds()
    {
        var sut = ReplyingJson(Sample("override-response.approve.json"));

        var result = await Approve(sut);

        Assert.True(result.IsSuccess);
        Assert.Equal(68400m, result.Value.Deposit!.Amount);
    }

    [Fact]
    public async Task Reject_MatchingResponse_Succeeds()
    {
        var sut = ReplyingJson(Sample("override-response.reject.json"));

        var result = await Reject(sut);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Deposit);
    }

    [Fact]
    public async Task Approve_ZeroDeposit_IsValid()
    {
        var sut = ReplyingJson(Sample("override-response.approve.json", j =>
            j["deposit"] = new JsonObject { ["rate"] = 0, ["amount"] = 0, ["currency"] = "VND" }));

        Assert.True((await Approve(sut)).IsSuccess);
    }

    [Fact]
    public async Task DecisionIdWithDifferentCase_IsStillTheSameDecision()
    {
        var sut = ReplyingJson(Sample("override-response.approve.json", j =>
            j["decisionId"] = TadssTestData.DecisionId.ToString().ToUpperInvariant()));

        Assert.True((await Approve(sut)).IsSuccess);
    }

    // ── Ghi đè hỏng ⇒ InvalidResponse ──────────────────────────────────────

    [Fact]
    public async Task Override_OfAnotherDecision_IsInvalidAndLogsBothIds()
    {
        var other = Guid.NewGuid();
        var sut = ReplyingJson(Sample("override-response.approve.json", j => j["decisionId"] = other.ToString()));

        var result = await Approve(sut);

        AssertInvalid(sut, result, other.ToString());
        Assert.Contains(TadssTestData.DecisionId.ToString(), Assert.Single(sut.Logger.Entries, e => e.Level == LogLevel.Error).Message);
    }

    [Fact]
    public async Task Override_WithAnotherAction_IsInvalid()
    {
        var sut = ReplyingJson(Sample("override-response.approve.json")); // gửi REJECT, nhận APPROVE

        AssertInvalid(sut, await Reject(sut), "action");
    }

    [Fact]
    public async Task Approve_WithoutDeposit_IsInvalid()
    {
        var sut = ReplyingJson(Sample("override-response.approve.json", j => j["deposit"] = null));

        AssertInvalid(sut, await Approve(sut), "thiếu deposit");
    }

    [Fact]
    public async Task Reject_WithDeposit_IsInvalid()
    {
        var sut = ReplyingJson(Sample("override-response.reject.json", j =>
            j["deposit"] = new JsonObject { ["rate"] = 0.3, ["amount"] = 68400, ["currency"] = "VND" }));

        AssertInvalid(sut, await Reject(sut), "có deposit");
    }

    [Theory]
    [InlineData(-0.01, 100, "deposit.rate")]
    [InlineData(1.01, 100, "deposit.rate")]
    [InlineData(0.3, -1, "deposit.amount")]
    public async Task Approve_DepositOutOfSchemaRange_IsInvalid(double rate, double amount, string fragment)
    {
        var sut = ReplyingJson(Sample("override-response.approve.json", j =>
            j["deposit"] = new JsonObject { ["rate"] = rate, ["amount"] = amount, ["currency"] = "VND" }));

        AssertInvalid(sut, await Approve(sut), fragment);
    }

    // ── Chi tiết quyết định ────────────────────────────────────────────────

    [Fact]
    public async Task GetDecision_MatchingId_Succeeds()
    {
        var sut = ReplyingJson(Sample("decision-detail.json"));

        Assert.True((await sut.Client.GetDecisionAsync(TadssTestData.DecisionId)).IsSuccess);
    }

    [Fact]
    public async Task GetDecision_OfAnotherDecision_IsInvalid()
    {
        var other = Guid.NewGuid();
        var sut = ReplyingJson(Sample("decision-detail.json", j => j["decisionId"] = other.ToString()));

        AssertInvalid(sut, await sut.Client.GetDecisionAsync(TadssTestData.DecisionId), other.ToString());
    }

    // ── Tên trường lỗi ─────────────────────────────────────────────────────

    [Fact]
    public async Task ValidationProblem_KeepsFieldNames()
    {
        var sut = Replying(() => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest,
            """{"title":"lỗi","status":400,"code":"VALIDATION_ERROR","errors":{"deposit.rate":["deposit.rate phải nằm trong [0, 1]."],"reason":["x"]}}""",
            "application/problem+json"));

        var result = await Approve(sut);

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Equal(new[] { "deposit.rate", "reason" }, result.ErrorFields.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProblemWithoutErrors_HasNoFieldNames()
    {
        var sut = Replying(() => FakeHttpMessageHandler.Problem(HttpStatusCode.BadRequest, "MISSING_HEADER"));

        Assert.Empty((await Approve(sut)).ErrorFields);
    }

    [Fact]
    public async Task NonProblemBody_HasNoFieldNames()
    {
        var sut = Replying(() => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "<html>Tunnel not found</html>", "text/html"));

        var result = await Approve(sut);

        Assert.Equal(404, result.HttpStatus);
        Assert.Null(result.ErrorCode);
        Assert.Empty(result.ErrorFields);
    }

    [Fact]
    public async Task Success_HasNoFieldNames()
    {
        var sut = ReplyingJson(Sample("override-response.approve.json"));

        Assert.Empty((await Approve(sut)).ErrorFields);
    }
}
