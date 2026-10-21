using Microsoft.Extensions.Logging;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — quyết định nhánh của đơn online (spec Phase 6, 7.6); chưa ghi DB. BookingService áp quyết định này ở transaction 2.
    /// </summary>
    /// <param name="Branch">Nhánh a, a′, b, c hoặc d</param>
    /// <param name="DepositSource">Nguồn chính sách cọc (TADSS hoặc FULL_PAYMENT_FALLBACK); null ở nhánh b, c (chưa có mức cọc)</param>
    /// <param name="DepositRate">Tỷ lệ cọc áp dụng (a′: 0, d: 1); 0 ở nhánh b, c (không dùng)</param>
    /// <param name="DepositAmount">Số cọc áp dụng và số tiền VNPay (a, d); 0 ở a′, b, c</param>
    /// <param name="OnlineAmount">Số tiền cần thu online dùng để so với mức tối thiểu; null ở nhánh b, c (không xét tiền)</param>
    /// <param name="TadssDecisionId">Id quyết định TADSS; null khi không có quyết định (cờ tắt hoặc TADSS lỗi)</param>
    /// <param name="ApprovalExpiresAtUtc">Hạn duyệt (UTC, Kind = Utc); chỉ có ở nhánh b</param>
    /// <param name="ApprovalReasonCode">Mã lý do cần duyệt; chỉ có ở nhánh b</param>
    /// <param name="ApprovalReasons">Lý do cần duyệt; rỗng ngoài nhánh b</param>
    /// <param name="LogLevel">Mức log caller nên ghi (None = không ghi)</param>
    /// <param name="LogMessage">Nội dung log; null khi LogLevel = None</param>
    public sealed record DepositDecision(
        DepositBranch Branch,
        DepositSource? DepositSource,
        decimal DepositRate,
        decimal DepositAmount,
        decimal? OnlineAmount,
        Guid? TadssDecisionId,
        DateTime? ApprovalExpiresAtUtc,
        string? ApprovalReasonCode,
        IReadOnlyList<string> ApprovalReasons,
        LogLevel LogLevel,
        string? LogMessage);
}
