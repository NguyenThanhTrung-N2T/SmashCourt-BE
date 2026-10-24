using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.Deposit;

/// <summary>
/// Nhánh xử lý đơn online đã chọn từ quyết định cọc, chưa ghi DB. Trường không dùng ở một nhánh mang giá trị 0 hoặc null.
/// </summary>
/// <param name="OnlineAmount">Số tiền cần thu online đem so với mức tối thiểu; null ở nhánh chờ duyệt và từ chối</param>
/// <param name="TadssDecisionId">Null khi không có quyết định (cờ tắt hoặc TADSS lỗi)</param>
/// <param name="LogLevel">Mức log caller nên ghi; None = không ghi</param>
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
