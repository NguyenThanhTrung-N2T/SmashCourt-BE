using System.Text.Json;
using System.Text.Json.Nodes;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integrations;

/// <summary>
/// CT-04: DTO của BE tương thích JSON schema v2 của TADSS (BE không reference SDK nên đây là chốt tương thích duy nhất).
/// Chiều nhận: mẫu response hợp lệ schema rồi parse đủ trường. Chiều gửi: request do DTO serialize hợp lệ schema.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "CT-04")]
public class TadssContractTests
{
    private static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, TadssJson.Options) ?? throw new InvalidOperationException("Deserialize trả null");

    private static void AssertValid(string schemaFile, string json)
    {
        var result = TadssContractFiles.Evaluate(schemaFile, json);
        Assert.True(result.IsValid, $"{schemaFile}: {TadssContractFiles.Describe(result)}");
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), TadssJson.Options);

    // ── Chiều gửi ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SerializeRequest_WhenDecisionRequestBuiltFromDto_IsValidAgainstRequestSchema(bool full)
    {
        var json = Serialize(TadssTestData.DecisionRequest(full));

        AssertValid(TadssContractFiles.DecisionRequest, json);
    }

    [Fact]
    public void SerializeRequest_WhenDecisionRequestHasOffsets_KeepsOffsetsAndEnumNames()
    {
        var node = JsonNode.Parse(Serialize(TadssTestData.DecisionRequest(full: true)))!;

        Assert.Equal("2026-11-20T18:00:00+07:00", node["items"]![0]!["startTime"]!.GetValue<string>());
        Assert.Equal("ONLINE", node["channel"]!.GetValue<string>());
        Assert.Equal("GOLD", node["customer"]!["tier"]!.GetValue<string>());
        Assert.Equal(240000m, node["items"]![0]!["price"]!.GetValue<decimal>());
    }

    [Fact]
    public void SerializeRequest_WhenOptionalFieldsAreNull_OmitsTheirKeys()
    {
        var node = JsonNode.Parse(Serialize(TadssTestData.DecisionRequest(full: false)))!.AsObject();

        Assert.False(node.ContainsKey("demand"));
        Assert.False(node.ContainsKey("metadata"));
        Assert.False(node["customer"]!.AsObject().ContainsKey("history"));
    }

    [Theory]
    [InlineData("rate")]
    [InlineData("amount")]
    public void SerializeRequest_WhenOverrideApprove_IsValidAgainstOverrideSchema(string kind)
    {
        var deposit = kind == "rate"
            ? new TadssOverrideDepositDto { Rate = 0.3m }
            : new TadssOverrideDepositDto { Amount = 68400m };
        var request = new TadssOverrideRequestDto
        {
            Action = TadssOverrideAction.APPROVE, Deposit = deposit, Reason = "Khách quen, chi nhánh bảo lãnh"
        };

        AssertValid(TadssContractFiles.OverrideRequest, Serialize(request));
    }

    [Fact]
    public void SerializeRequest_WhenOverrideReject_OmitsDepositKeyAndIsValid()
    {
        var request = new TadssOverrideRequestDto { Action = TadssOverrideAction.REJECT, Reason = "Sân bảo trì vào giờ này" };

        var json = Serialize(request);

        Assert.False(JsonNode.Parse(json)!.AsObject().ContainsKey("deposit"));
        AssertValid(TadssContractFiles.OverrideRequest, json);
    }

    [Theory]
    [InlineData(TadssOutcome.COMPLETED)]
    [InlineData(TadssOutcome.NO_SHOW)]
    [InlineData(TadssOutcome.CANCELLED_BY_CUSTOMER)]
    [InlineData(TadssOutcome.CANCELLED_BY_STAFF)]
    [InlineData(TadssOutcome.PAYMENT_EXPIRED)]
    [InlineData(TadssOutcome.APPROVAL_EXPIRED)]
    [InlineData(TadssOutcome.REJECTED)]
    public void SerializeRequest_WhenOutcomeReport_IsValidAgainstOutcomeSchema(TadssOutcome outcome)
    {
        var request = new TadssOutcomeRequestDto
        {
            Outcome = outcome, OccurredAt = new DateTimeOffset(2026, 11, 20, 18, 30, 0, TimeSpan.FromHours(7))
        };

        AssertValid(TadssContractFiles.OutcomeRequest, Serialize(request));
    }

    // ── Chiều nhận ───────────────────────────────────────────────────────────────

    [Fact]
    public void ParseResult_WhenApproved_ReadsDepositAndTimes()
    {
        var json = TadssContractFiles.Sample("decision-result.approved.json");
        AssertValid(TadssContractFiles.DecisionResult, json);

        var result = Parse<TadssDecisionResultDto>(json);

        Assert.Equal(TadssDecisionStatus.APPROVED, result.Status);
        Assert.Equal(0.5m, result.Deposit!.Rate);
        Assert.Equal(114000m, result.Deposit.Amount);
        Assert.Equal("VND", result.Deposit.Currency);
        Assert.Equal("PEAK_HOUR_DEPOSIT", result.ReasonCode);
        Assert.False(result.FallbackUsed);
        Assert.Null(result.ApprovalExpiresAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 2, 15, 0, 41, TimeSpan.Zero), result.EvaluatedAt);
    }

    [Fact]
    public void ParseResult_WhenFallbackApproved_ReadsFallbackFlags()
    {
        var json = TadssContractFiles.Sample("decision-result.approved-fallback.json");
        AssertValid(TadssContractFiles.DecisionResult, json);

        var result = Parse<TadssDecisionResultDto>(json);

        Assert.Equal(TadssDecisionStatus.APPROVED, result.Status);
        Assert.True(result.FallbackUsed);
        Assert.Equal("FALLBACK", result.ExecutionType);
        Assert.Equal(1m, result.Deposit!.Rate);
        Assert.Equal(228000m, result.Deposit.Amount);
    }

    [Fact]
    public void ParseResult_WhenNeedApproval_ReadsExpiryWithOffsetAndUnknownRecommendation()
    {
        var json = TadssContractFiles.Sample("decision-result.need-approval.json");
        AssertValid(TadssContractFiles.DecisionResult, json);

        var result = Parse<TadssDecisionResultDto>(json);

        Assert.Equal(TadssDecisionStatus.NEED_APPROVAL, result.Status);
        Assert.Null(result.Deposit);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 14, 15, 0, TimeSpan.FromHours(7)), result.ApprovalExpiresAt);
        Assert.Equal(TimeSpan.FromHours(7), result.ApprovalExpiresAt!.Value.Offset);
        Assert.Equal(["REQUIRE_APPROVAL", "ALTERNATIVE_SLOT"], result.Recommendations.Select(x => x.Type));
    }

    [Fact]
    public void ParseResult_WhenRecommendationTypeIsUnknown_StillParses()
    {
        var node = JsonNode.Parse(TadssContractFiles.Sample("decision-result.need-approval.json"))!;
        node["recommendations"]![0]!["type"] = "SOMETHING_NEW";

        var result = Parse<TadssDecisionResultDto>(node.ToJsonString());

        Assert.Equal("SOMETHING_NEW", result.Recommendations[0].Type);
    }

    [Fact]
    public void ParseResult_WhenRejected_HasNoDeposit()
    {
        var json = TadssContractFiles.Sample("decision-result.rejected.json");
        AssertValid(TadssContractFiles.DecisionResult, json);

        var result = Parse<TadssDecisionResultDto>(json);

        Assert.Equal(TadssDecisionStatus.REJECTED, result.Status);
        Assert.Null(result.Deposit);
        Assert.Null(result.ApprovalExpiresAt);
        Assert.Equal("REPEATED_PAYMENT_EXPIRY", result.ReasonCode);
    }

    [Fact]
    public void ParseResult_WhenReasonCodeIsUnknown_StillParses()
    {
        var node = JsonNode.Parse(TadssContractFiles.Sample("decision-result.approved.json"))!;
        node["reasonCode"] = "BRAND_NEW_REASON_FROM_FUTURE_STRATEGY";

        var result = Parse<TadssDecisionResultDto>(node.ToJsonString());

        Assert.Equal("BRAND_NEW_REASON_FROM_FUTURE_STRATEGY", result.ReasonCode);
    }

    [Fact]
    public void ParseResult_WhenOverrideApprove_ReadsConvertedDeposit()
    {
        var json = TadssContractFiles.Sample("override-response.approve.json");
        AssertValid(TadssContractFiles.OverrideResponse, json);

        var result = Parse<TadssOverrideResponseDto>(json);

        Assert.Equal(TadssOverrideAction.APPROVE, result.Action);
        Assert.Equal(0.3m, result.Deposit!.Rate);
        Assert.Equal(68400m, result.Deposit.Amount);
        Assert.Equal("Khách quen, chi nhánh bảo lãnh", result.Reason);
    }

    [Fact]
    public void ParseResult_WhenOverrideReject_HasNoDeposit()
    {
        var json = TadssContractFiles.Sample("override-response.reject.json");
        AssertValid(TadssContractFiles.OverrideResponse, json);

        var result = Parse<TadssOverrideResponseDto>(json);

        Assert.Equal(TadssOverrideAction.REJECT, result.Action);
        Assert.Null(result.Deposit);
    }

    [Fact]
    public void ParseResult_WhenOutcome_ReadsOutcomeAndTimes()
    {
        var json = TadssContractFiles.Sample("outcome-response.json");
        AssertValid(TadssContractFiles.OutcomeResponse, json);

        var result = Parse<TadssOutcomeResponseDto>(json);

        Assert.Equal(TadssOutcome.NO_SHOW, result.Outcome);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 11, 30, 0, TimeSpan.Zero), result.OccurredAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 19, 0, 2, TimeSpan.Zero), result.ReportedAt);
    }

    [Fact]
    public void ParseResult_WhenDecisionDetail_ReadsOverrideAndOutcome()
    {
        var json = TadssContractFiles.Sample("decision-detail.json");
        AssertValid(TadssContractFiles.DecisionDetail, json);

        var detail = Parse<TadssDecisionDetailDto>(json);

        Assert.Equal(TadssDecisionStatus.NEED_APPROVAL, detail.Result.Status);
        Assert.Equal(TadssDecisionStatus.NEED_APPROVAL, detail.Result.StrategyStatus);
        Assert.Equal(TadssOverrideAction.APPROVE, detail.Override!.Action);
        Assert.Equal(TadssOutcome.COMPLETED, detail.Outcome!.Outcome);
        Assert.Equal(12, detail.Request.Customer.History!.CompletedCount);
        Assert.Equal(3, detail.Execution.ExecutionTimeMs);
        Assert.Null(detail.Execution.FailureReason);
    }

    // ── Dữ liệu sai hình thức ────────────────────────────────────────────────────

    [Fact]
    public void ParseResult_WhenStatusIsUnknown_ThrowsJsonException()
    {
        var node = JsonNode.Parse(TadssContractFiles.Sample("decision-result.approved.json"))!;
        node["status"] = "MAYBE";

        Assert.Throws<JsonException>(() => Parse<TadssDecisionResultDto>(node.ToJsonString()));
    }

    [Fact]
    public void ParseResult_WhenEnumIsNumber_ThrowsJsonException()
    {
        var node = JsonNode.Parse(TadssContractFiles.Sample("decision-result.approved.json"))!;
        node["status"] = 0;

        Assert.Throws<JsonException>(() => Parse<TadssDecisionResultDto>(node.ToJsonString()));
    }

    [Fact]
    public void ParseResult_WhenRequiredFieldIsMissing_ThrowsJsonException()
    {
        var node = JsonNode.Parse(TadssContractFiles.Sample("decision-result.approved.json"))!.AsObject();
        node.Remove("decisionId");

        Assert.Throws<JsonException>(() => Parse<TadssDecisionResultDto>(node.ToJsonString()));
    }

    // ── Lỗi RFC 7807 (§3.8) ──────────────────────────────────────────────────────

    public static IEnumerable<object[]> ProblemRows()
    {
        var rows = JsonNode.Parse(TadssContractFiles.Sample("problems.json"))!.AsArray();
        for (var i = 0; i < rows.Count; i++)
            yield return [i];
    }

    [Theory]
    [MemberData(nameof(ProblemRows))]
    public void ParseProblem_WhenRowOfErrorTable_ReadsCodeAndRetryAfter(int index)
    {
        var row = JsonNode.Parse(TadssContractFiles.Sample("problems.json"))!.AsArray()[index]!;

        var problem = Parse<TadssProblemDto>(row["body"]!.ToJsonString());

        Assert.Equal(row["status"]!.GetValue<int>(), problem.Status);
        Assert.Equal(row["code"]?.GetValue<string>(), problem.Code);
        Assert.Equal(row["retryAfterSeconds"]?.GetValue<int>(), problem.RetryAfterSeconds);
    }

    [Fact]
    public void ParseProblem_WhenErrorTableIsComplete_CoversEveryCodeOfSection38()
    {
        string[] expected =
        [
            "VALIDATION_ERROR", "DOMAIN_INVARIANT_VIOLATION", "MISSING_HEADER", "UNAUTHORIZED", "DECISION_NOT_FOUND",
            "IDEMPOTENCY_KEY_REUSE", "NOT_PENDING_APPROVAL", "ALREADY_OVERRIDDEN", "APPROVAL_EXPIRED",
            "OUTCOME_ALREADY_RECORDED", "PERSISTENCE_UNAVAILABLE", "GATEWAY_TIMEOUT", "INTERNAL_ERROR",
            "PAYLOAD_TOO_LARGE", "RATE_LIMITED"
        ];
        var codes = JsonNode.Parse(TadssContractFiles.Sample("problems.json"))!.AsArray()
            .Select(x => x!["code"]?.GetValue<string>()).ToList();

        Assert.All(expected, code => Assert.Contains(code, codes));
        Assert.Contains(codes, code => code is null); // 415 của MVC không có code
    }

    [Fact]
    public void ParseProblem_WhenValidationError_ReadsFieldErrors()
    {
        var row = JsonNode.Parse(TadssContractFiles.Sample("problems.json"))!.AsArray()[0]!;

        var problem = Parse<TadssProblemDto>(row["body"]!.ToJsonString());

        Assert.Equal(["Thiếu trường bắt buộc."], problem.Errors!["items"]);
    }
}
