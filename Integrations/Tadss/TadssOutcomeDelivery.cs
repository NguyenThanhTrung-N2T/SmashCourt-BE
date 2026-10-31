using SmashCourt_BE.Integrations.Tadss.DTOs;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Phân loại kết quả gửi outcome (C3 — P6-26). Chỉ khi TADSS trả đúng mã lỗi của contract cho đúng status mới coi là lỗi riêng một đơn;
/// mọi thứ khác (kể cả 404 trang lỗi HTML của ngrok, không có mã) là hệ thống chưa sẵn sàng: gửi tiếp các đơn còn lại cũng lỗi y hệt.
/// </summary>
public static class TadssOutcomeDelivery
{
    private const string OutcomeAlreadyRecorded = "OUTCOME_ALREADY_RECORDED";
    private const string DecisionNotFound = "DECISION_NOT_FOUND";
    private const string ValidationError = "VALIDATION_ERROR";
    private const string DomainInvariantViolation = "DOMAIN_INVARIANT_VIOLATION";

    public static TadssOutcomeDeliveryKind Classify(TadssCallResult<TadssOutcomeResponseDto> result)
    {
        if (result.IsSuccess)
            return TadssOutcomeDeliveryKind.Recorded;
        if (result.FailureKind != TadssFailureKind.Http)
            return TadssOutcomeDeliveryKind.SystemFailure;

        return (result.HttpStatus, result.ErrorCode) switch
        {
            // Đã có outcome khác nội dung: TADSS giữ bản đầu (một outcome mỗi quyết định), gửi lại không đổi được gì
            (409, OutcomeAlreadyRecorded) => TadssOutcomeDeliveryKind.Recorded,
            (404, DecisionNotFound) => TadssOutcomeDeliveryKind.Rejected,
            (400, ValidationError or DomainInvariantViolation) => TadssOutcomeDeliveryKind.Rejected,
            _ => TadssOutcomeDeliveryKind.SystemFailure
        };
    }
}
