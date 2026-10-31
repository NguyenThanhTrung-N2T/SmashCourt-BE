using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// Phân loại kết quả gửi outcome (gói 6A-5 Task 6, C3 — P6-26): chỉ khi TADSS thật sự trả lời "quyết định này không có / nội dung này sai"
/// (mã lỗi của contract) mới là lỗi riêng một đơn; mọi thứ khác (timeout, mạng, 5xx, 429, 401, trang lỗi HTML của ngrok…) là hệ thống
/// chưa sẵn sàng ⇒ dừng chu kỳ.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
public class TadssOutcomeDeliveryTests
{
    private const string CorrelationId = "corr";

    private static TadssCallResult<TadssOutcomeResponseDto> Http(int status, string? code = null) =>
        TadssCallResult<TadssOutcomeResponseDto>.Failure(TadssFailureKind.Http, CorrelationId, status, code);

    [Fact]
    public void Classify_Success_IsRecorded()
    {
        var result = TadssCallResult<TadssOutcomeResponseDto>.Success(new TadssOutcomeResponseDto
        {
            DecisionId = Guid.NewGuid(), Outcome = TadssOutcome.COMPLETED, OccurredAt = DateTimeOffset.UtcNow, ReportedAt = DateTimeOffset.UtcNow
        }, 200, CorrelationId);

        Assert.Equal(TadssOutcomeDeliveryKind.Recorded, TadssOutcomeDelivery.Classify(result));
    }

    [Fact]
    public void Classify_OutcomeAlreadyRecorded_IsRecorded()
    {
        Assert.Equal(TadssOutcomeDeliveryKind.Recorded, TadssOutcomeDelivery.Classify(Http(409, "OUTCOME_ALREADY_RECORDED")));
    }

    [Theory]
    [InlineData(404, "DECISION_NOT_FOUND")]
    [InlineData(400, "VALIDATION_ERROR")]
    [InlineData(400, "DOMAIN_INVARIANT_VIOLATION")]
    public void Classify_TadssRejectsThisOrder_IsRejected(int status, string code)
    {
        Assert.Equal(TadssOutcomeDeliveryKind.Rejected, TadssOutcomeDelivery.Classify(Http(status, code)));
    }

    [Theory]
    [InlineData(404, null)]                       // trang lỗi HTML của ngrok, không có mã
    [InlineData(400, null)]
    [InlineData(400, "MISSING_HEADER")]           // BE gửi thiếu header: lỗi của BE, mọi đơn đều lỗi
    [InlineData(401, "INVALID_API_KEY")]
    [InlineData(401, null)]
    [InlineData(409, "ALREADY_OVERRIDDEN")]       // 409 mã khác
    [InlineData(409, null)]
    [InlineData(413, "PAYLOAD_TOO_LARGE")]
    [InlineData(415, null)]
    [InlineData(429, "RATE_LIMITED")]
    [InlineData(500, "INTERNAL_ERROR")]
    [InlineData(502, null)]
    [InlineData(503, "PERSISTENCE_UNAVAILABLE")]
    [InlineData(504, "DECISION_TIMEOUT")]
    [InlineData(404, "VALIDATION_ERROR")]         // mã đúng nhưng sai status: không tin
    [InlineData(400, "DECISION_NOT_FOUND")]
    public void Classify_AnythingElseOverHttp_IsSystemFailure(int status, string? code)
    {
        Assert.Equal(TadssOutcomeDeliveryKind.SystemFailure, TadssOutcomeDelivery.Classify(Http(status, code)));
    }

    [Theory]
    [InlineData(TadssFailureKind.Timeout)]
    [InlineData(TadssFailureKind.Network)]
    [InlineData(TadssFailureKind.InvalidResponse)]
    public void Classify_NoUsableAnswer_IsSystemFailure(TadssFailureKind kind)
    {
        var result = TadssCallResult<TadssOutcomeResponseDto>.Failure(kind, CorrelationId, kind == TadssFailureKind.InvalidResponse ? 200 : null);

        Assert.Equal(TadssOutcomeDeliveryKind.SystemFailure, TadssOutcomeDelivery.Classify(result));
    }
}
