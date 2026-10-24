using System.Globalization;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.Deposit;

/// <summary>
/// Chọn nhánh xử lý đơn online từ kết quả TADSS. Hàm thuần: không đọc DB, đồng hồ hay cấu hình.
/// Thứ tự: cờ tắt hoặc TADSS lỗi ⇒ dự phòng 100 %; REJECTED / NEED_APPROVAL không xét tiền;
/// APPROVED ⇒ cọc của TADSS; số tiền cần thu online dưới mức tối thiểu ⇒ xác nhận, thu tại quầy.
/// </summary>
public static class DepositDecisionResolver
{
    // Request luôn gửi VND nên quyết định phải cùng đơn vị tiền
    private const string RequestCurrency = "VND";

    // tadssResult null = không gọi TADSS (cờ EnableTadssDecision tắt)
    public static DepositDecision Resolve(
        TadssCallResult<TadssDecisionResultDto>? tadssResult, decimal finalTotal, decimal minOnlinePaymentAmount)
    {
        if (finalTotal < 0)
            throw new ArgumentOutOfRangeException(nameof(finalTotal), finalTotal, "Tổng tiền không được âm.");
        if (minOnlinePaymentAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(minOnlinePaymentAmount), minOnlinePaymentAmount, "Mức tối thiểu phải lớn hơn 0.");

        if (tadssResult == null)
            return Fallback(finalTotal, minOnlinePaymentAmount, LogLevel.None, null);

        if (!tadssResult.IsSuccess)
            return Fallback(finalTotal, minOnlinePaymentAmount, FailureLevel(tadssResult), DescribeFailure(tadssResult));

        var decision = tadssResult.Value;
        switch (decision.Status)
        {
            case TadssDecisionStatus.REJECTED:
                return Rejected(decision);

            case TadssDecisionStatus.NEED_APPROVAL:
                // Client đã kiểm; kiểm lại vì thiếu hạn duyệt thì không có đường duyệt hợp lệ
                if (decision.ApprovalExpiresAt is not { } approvalExpiresAt)
                    return Fallback(finalTotal, minOnlinePaymentAmount, LogLevel.Error,
                        $"TADSS trả NEED_APPROVAL nhưng thiếu approvalExpiresAt (decisionId {decision.DecisionId}); dùng phương án dự phòng.");
                return NeedApproval(decision, approvalExpiresAt);

            case TadssDecisionStatus.APPROVED:
                if (decision.Deposit == null)
                    return Fallback(finalTotal, minOnlinePaymentAmount, LogLevel.Error,
                        $"TADSS trả APPROVED nhưng thiếu deposit (decisionId {decision.DecisionId}); dùng phương án dự phòng.");
                return Approved(decision, decision.Deposit, finalTotal, minOnlinePaymentAmount);

            default:
                throw new ArgumentOutOfRangeException(nameof(tadssResult), decision.Status, "Trạng thái quyết định TADSS không hợp lệ.");
        }
    }

    private static DepositDecision Approved(TadssDecisionResultDto decision, TadssDepositDto deposit, decimal finalTotal, decimal min)
    {
        // Thu online quá tổng đơn hoặc sai đơn vị tiền là mất tiền thật: không dùng quyết định này
        if (deposit.Currency != RequestCurrency || deposit.Amount > finalTotal)
            return Fallback(finalTotal, min, LogLevel.Error,
                $"TADSS trả cọc {Format(deposit.Amount)} {deposit.Currency} không khớp đơn {Format(finalTotal)} {RequestCurrency} " +
                $"(decisionId {decision.DecisionId}); dùng phương án dự phòng.");

        if (deposit.Amount < min)
        {
            return new DepositDecision(
                DepositBranch.ConfirmWithoutOnlinePayment, DepositSource.TADSS, 0m, 0m, deposit.Amount, decision.DecisionId,
                null, null, [], LogLevel.Warning,
                $"TADSS quyết định cọc {Format(deposit.Amount)} nhỏ hơn mức thanh toán online tối thiểu {Format(min)}: " +
                $"đơn được xác nhận, thu toàn bộ tại quầy (decisionId {decision.DecisionId}).");
        }

        return new DepositDecision(
            DepositBranch.TadssDeposit, DepositSource.TADSS, deposit.Rate, deposit.Amount, deposit.Amount, decision.DecisionId,
            null, null, [], LogLevel.None, null);
    }

    private static DepositDecision NeedApproval(TadssDecisionResultDto decision, DateTimeOffset approvalExpiresAt) => new(
        DepositBranch.NeedApproval, null, 0m, 0m, null, decision.DecisionId,
        approvalExpiresAt.UtcDateTime, decision.ReasonCode, (decision.Reasons ?? []).ToList(), LogLevel.Information,
        $"TADSS yêu cầu duyệt cọc (decisionId {decision.DecisionId}, mã {decision.ReasonCode}).");

    private static DepositDecision Rejected(TadssDecisionResultDto decision) => new(
        DepositBranch.Rejected, null, 0m, 0m, null, decision.DecisionId,
        null, null, [], LogLevel.Information,
        $"TADSS từ chối đơn (decisionId {decision.DecisionId}, mã {decision.ReasonCode}).");

    // Không có quyết định nên không mang decisionId; 100 % của số tiền nhỏ không thu online được nên xác nhận, thu tại quầy
    private static DepositDecision Fallback(decimal finalTotal, decimal min, LogLevel level, string? message)
    {
        if (finalTotal >= min)
        {
            return new DepositDecision(
                DepositBranch.FullPaymentFallback, DepositSource.FULL_PAYMENT_FALLBACK, 1m, finalTotal, finalTotal, null,
                null, null, [], level, message);
        }

        var small = $"Tổng tiền {Format(finalTotal)} nhỏ hơn mức thanh toán online tối thiểu {Format(min)}: " +
                    "đơn được xác nhận, thu toàn bộ tại quầy.";
        return new DepositDecision(
            DepositBranch.ConfirmWithoutOnlinePayment, DepositSource.FULL_PAYMENT_FALLBACK, 0m, 0m, finalTotal, null,
            null, null, [], AtLeastWarning(level), message == null ? small : $"{message} {small}");
    }

    // 400/409 bất thường là lỗi của BE (request sai, dùng lại khóa) nên Error; lỗi hạ tầng còn lại là Warning
    private static LogLevel FailureLevel(TadssCallResult<TadssDecisionResultDto> result) =>
        result.FailureKind == TadssFailureKind.Http && result.HttpStatus is 400 or 409 ? LogLevel.Error : LogLevel.Warning;

    private static string DescribeFailure(TadssCallResult<TadssDecisionResultDto> result) =>
        $"TADSS lỗi ({result.FailureKind}, HTTP {result.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "-"}, " +
        $"code {result.ErrorCode ?? "-"}, correlation {result.CorrelationId}); dùng phương án dự phòng.";

    // LogLevel.None có giá trị lớn nhất nên không so sánh bằng toán tử
    private static LogLevel AtLeastWarning(LogLevel level) =>
        level is LogLevel.Error or LogLevel.Critical ? level : LogLevel.Warning;

    private static string Format(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);
}
