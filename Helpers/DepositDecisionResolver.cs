using System.Globalization;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Chọn nhánh a, a′, b, c, d của đơn online từ kết quả gọi TADSS (spec Phase 6, 7.6). Hàm thuần: không đọc DB,
    /// không đọc đồng hồ, không đọc cấu hình.
    /// <para>
    /// Thứ tự: (1) cờ tắt và (2) mọi lỗi TADSS ⇒ chính sách dự phòng 100 %, số tiền cần thu online = <c>finalTotal</c>;
    /// (3) REJECTED ⇒ c, NEED_APPROVAL ⇒ b, hai nhánh này <b>không xét tiền</b> nên đơn 0 đ vẫn bị từ chối hoặc chờ duyệt;
    /// (4) APPROVED ⇒ chính sách TADSS, số tiền cần thu online = <c>deposit.amount</c>; (5) số tiền cần thu online nhỏ hơn
    /// mức tối thiểu ⇒ a′, ngược lại a (TADSS) hoặc d (dự phòng).
    /// </para>
    /// </summary>
    public static class DepositDecisionResolver
    {
        // Request luôn gửi VND (TadssDecisionRequestMapper); quyết định phải cùng đơn vị tiền
        private const string RequestCurrency = "VND";

        /// <param name="tadssResult">Kết quả gọi TADSS; null khi không gọi (cờ <c>EnableTadssDecision</c> tắt).</param>
        /// <param name="finalTotal">Tổng tiền hóa đơn lúc đặt</param>
        /// <param name="minOnlinePaymentAmount">Số tiền thanh toán online tối thiểu (&gt; 0)</param>
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
                    // Client đã kiểm INV-7; kiểm lại để không dựa vào chỗ khác (thiếu hạn duyệt thì không có đường duyệt hợp lệ)
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
            // F1: quyết định không khớp đơn (TADSS tính sai) thì không dùng: thu online quá tổng đơn hoặc sai đơn vị tiền là mất tiền thật
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

        // Cờ tắt hoặc TADSS lỗi: không có quyết định nên không mang decisionId; 100 % của số nhỏ không thu online được nên chuyển a′
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

        // 400/409 bất thường là lỗi của BE (request sai, dùng lại idempotency key) nên Error; các lỗi còn lại là sự cố hạ tầng nên Warning
        private static LogLevel FailureLevel(TadssCallResult<TadssDecisionResultDto> result) =>
            result.FailureKind == TadssFailureKind.Http && result.HttpStatus is 400 or 409 ? LogLevel.Error : LogLevel.Warning;

        private static string DescribeFailure(TadssCallResult<TadssDecisionResultDto> result) =>
            $"TADSS lỗi ({result.FailureKind}, HTTP {result.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "-"}, " +
            $"code {result.ErrorCode ?? "-"}, correlation {result.CorrelationId}); dùng phương án dự phòng.";

        // LogLevel.None (6) lớn hơn mọi mức khác nên không so sánh bằng toán tử
        private static LogLevel AtLeastWarning(LogLevel level) =>
            level is LogLevel.Error or LogLevel.Critical ? level : LogLevel.Warning;

        private static string Format(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
