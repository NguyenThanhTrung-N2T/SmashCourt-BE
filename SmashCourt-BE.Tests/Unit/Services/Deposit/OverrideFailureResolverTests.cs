using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Tests.TestData;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Common;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;

namespace SmashCourt_BE.Tests.Unit.Services.Deposit;

/// <summary>
/// BE-UT-04: lỗi TADSS khi duyệt / từ chối ⇒ mã BE (DEPOSIT_APPROVAL_API Mục 5, cộng ba dòng chốt 24/10: 403 và 404 không mã ⇒ 503,
/// response 2xx hỏng ⇒ 503, 409 mã lạ ⇒ 500). Không dòng nào dẫn tới cọc 100 % hay đổi trạng thái đơn.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Category", TestCategories.Helper)]
[Trait("Catalog", "BE-UT-04")]
public class OverrideFailureResolverTests
{
    private static TadssCallResult<TadssOverrideResponseDto> Http(int status, string? code = null, params string[] fields) =>
        TadssCallResult<TadssOverrideResponseDto>.Failure(TadssFailureKind.Http, "corr-1", status, code, errorFields: fields);

    private static TadssCallResult<TadssOverrideResponseDto> Kind(TadssFailureKind kind, int? status = null) =>
        TadssCallResult<TadssOverrideResponseDto>.Failure(kind, "corr-1", status);

    private static void AssertUnavailable(TadssCallResult<TadssOverrideResponseDto> failure, LogLevel level)
    {
        var result = OverrideFailureResolver.Resolve(failure);

        Assert.Equal(503, result.Error.StatusCode);
        Assert.Equal(ErrorCodes.TadssUnavailable, result.Error.ErrorCode);
        Assert.Equal("Hệ thống duyệt cọc tạm thời không phản hồi. Đơn vẫn đang chờ duyệt, vui lòng thử lại sau ít phút.", result.Error.Message);
        Assert.False(result.SyncStoredOverride);
        Assert.Equal(level, result.LogLevel);
    }

    private static void AssertConflict(TadssCallResult<TadssOverrideResponseDto> failure, string code, bool sync = false)
    {
        var result = OverrideFailureResolver.Resolve(failure);

        Assert.Equal(409, result.Error.StatusCode);
        Assert.Equal(code, result.Error.ErrorCode);
        Assert.Equal(sync, result.SyncStoredOverride);
        Assert.Equal(LogLevel.Warning, result.LogLevel);
    }

    private static void AssertInternal(TadssCallResult<TadssOverrideResponseDto> failure)
    {
        var result = OverrideFailureResolver.Resolve(failure);

        Assert.Equal(500, result.Error.StatusCode);
        Assert.Equal(ErrorCodes.InternalError, result.Error.ErrorCode);
        Assert.False(result.SyncStoredOverride);
        Assert.Equal(LogLevel.Error, result.LogLevel);
    }

    // ── 503: tạm thời (Mục 5 dòng 13–15) ─────────────────────────────────

    [Theory]
    [InlineData(TadssFailureKind.Timeout)]
    [InlineData(TadssFailureKind.Network)]
    public void TimeoutOrNetwork_IsUnavailableWarning(TadssFailureKind kind) => AssertUnavailable(Kind(kind), LogLevel.Warning);

    [Theory]
    [InlineData(500, "INTERNAL_ERROR")]
    [InlineData(503, "PERSISTENCE_UNAVAILABLE")]
    [InlineData(504, "GATEWAY_TIMEOUT")]
    [InlineData(502, null)]
    [InlineData(429, "RATE_LIMITED")]
    public void ServerErrorOrRateLimit_IsUnavailableWarning(int status, string? code) => AssertUnavailable(Http(status, code), LogLevel.Warning);

    // ── 503: cấu hình / đường đi hỏng (dòng 16 + chốt 24/10) ──────────────

    [Theory]
    [InlineData(401, "UNAUTHORIZED")]
    [InlineData(403, null)]
    [InlineData(404, null)]         // VD trang lỗi của ngrok khi URL đổi
    [InlineData(404, "NOT_FOUND")]  // mã không phải DECISION_NOT_FOUND
    public void ConfigurationOrRouteProblem_IsUnavailableError(int status, string? code) => AssertUnavailable(Http(status, code), LogLevel.Error);

    [Fact]
    public void Unusable2xxResponse_IsUnavailableError() =>
        AssertUnavailable(Kind(TadssFailureKind.InvalidResponse, 200), LogLevel.Error);

    // ── 409 theo mã TADSS (dòng 9–12) ─────────────────────────────────────

    [Fact]
    public void DecisionNotFound_IsApprovalDecisionNotFound() => AssertConflict(Http(404, "DECISION_NOT_FOUND"), ErrorCodes.ApprovalDecisionNotFound);

    [Fact]
    public void NotPendingApproval_IsBookingNotPendingApproval() => AssertConflict(Http(409, "NOT_PENDING_APPROVAL"), ErrorCodes.BookingNotPendingApproval);

    [Fact]
    public void ApprovalExpired_IsApprovalExpired() => AssertConflict(Http(409, "APPROVAL_EXPIRED"), ErrorCodes.ApprovalExpired);

    [Theory]
    [InlineData("ALREADY_OVERRIDDEN")]
    [InlineData("IDEMPOTENCY_KEY_REUSE")]
    public void AlreadyOverridden_SyncsStoredOverrideThenAlreadyResolved(string code) =>
        AssertConflict(Http(409, code), ErrorCodes.ApprovalAlreadyResolved, sync: true);

    [Theory]
    [InlineData("OUTCOME_ALREADY_RECORDED")]
    [InlineData(null)]
    public void UnknownConflict_IsInternalError(string? code) => AssertInternal(Http(409, code));

    // ── 400 (dòng 4, 5) ───────────────────────────────────────────────────

    [Theory]
    [InlineData("deposit.rate")]
    [InlineData("deposit.amount")]
    public void ValidationErrorOnDeposit_IsValidationErrorOnDepositRate(string field)
    {
        var result = OverrideFailureResolver.Resolve(Http(400, "VALIDATION_ERROR", field));

        Assert.Equal(400, result.Error.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, result.Error.ErrorCode);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", result.Error.Message);
        Assert.Equal([DepositApprovalRules.InvalidRateMessage], result.Error.Errors!["DepositRate"]);
        Assert.Single(result.Error.Errors);
        Assert.False(result.SyncStoredOverride);
        Assert.Equal(LogLevel.Error, result.LogLevel); // BE đã kiểm trước nên là lệch quy tắc
    }

    [Fact]
    public void ValidationErrorOnReason_IsInternalError() => AssertInternal(Http(400, "VALIDATION_ERROR", "reason"));

    [Fact]
    public void ValidationErrorWithoutFields_IsInternalError() => AssertInternal(Http(400, "VALIDATION_ERROR"));

    [Fact]
    public void DepositFieldWithOtherCode_IsInternalError() => AssertInternal(Http(400, "DOMAIN_INVARIANT_VIOLATION", "deposit.rate"));

    [Theory]
    [InlineData(400, "MISSING_HEADER")]
    [InlineData(400, "DOMAIN_INVARIANT_VIOLATION")]
    [InlineData(413, "PAYLOAD_TOO_LARGE")]
    [InlineData(415, null)]
    [InlineData(422, null)]
    public void ProgrammingError_IsInternalError(int status, string? code) => AssertInternal(Http(status, code));

    // ── Không bao giờ ─────────────────────────────────────────────────────

    [Fact]
    public void Success_IsNotAFailure()
    {
        var success = TadssCallResult<TadssOverrideResponseDto>.Success(
            new TadssOverrideResponseDto
            {
                DecisionId = Guid.NewGuid(), Action = TadssOverrideAction.REJECT, ActorId = "a", Reason = "r", OverriddenAt = DateTimeOffset.UtcNow
            }, 200, "corr-1");

        Assert.Throws<ArgumentException>(() => OverrideFailureResolver.Resolve(success));
    }

    [Fact]
    public void LogMessage_CarriesCorrelationIdStatusAndCode()
    {
        var result = OverrideFailureResolver.Resolve(Http(401, "UNAUTHORIZED"));

        Assert.Contains("corr-1", result.LogMessage);
        Assert.Contains("401", result.LogMessage);
        Assert.Contains("UNAUTHORIZED", result.LogMessage);
    }
}
