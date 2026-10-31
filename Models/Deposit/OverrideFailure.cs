using SmashCourt_BE.Common;

namespace SmashCourt_BE.Models.Deposit;

/// <summary>
/// Cách xử lý một lần duyệt / từ chối mà TADSS trả lỗi.
/// </summary>
/// <param name="SyncStoredOverride">TADSS đã có ghi đè: áp ghi đè đã lưu nếu đơn còn chờ, rồi vẫn trả <paramref name="Error"/></param>
public sealed record OverrideFailure(AppException Error, bool SyncStoredOverride, LogLevel LogLevel, string LogMessage);
