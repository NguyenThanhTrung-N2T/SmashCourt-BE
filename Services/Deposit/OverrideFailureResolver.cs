using SmashCourt_BE.Common;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Models.Deposit;

namespace SmashCourt_BE.Services.Deposit;

/// <summary>
/// Ánh xạ lỗi TADSS khi Quản lý duyệt / từ chối thành lỗi trả cho Quản lý. Khác lúc đặt sân: không có đường lùi cọc 100 %,
/// đơn luôn giữ nguyên trạng thái chờ duyệt. Hàm thuần.
/// </summary>
public static class OverrideFailureResolver
{
    public const string UnavailableMessage =
        "Hệ thống duyệt cọc tạm thời không phản hồi. Đơn vẫn đang chờ duyệt, vui lòng thử lại sau ít phút.";
    private const string InternalMessage = "Đã xảy ra lỗi hệ thống, vui lòng thử lại sau";
    private const string ValidationMessage = "Dữ liệu đầu vào không hợp lệ";

    // Khóa lỗi của TADSS là đường dẫn JSON; chỉ lỗi ở mức cọc là do Quản lý nhập sai
    private static readonly string[] DepositFields = ["deposit.rate", "deposit.amount"];

    public static OverrideFailure Resolve<T>(TadssCallResult<T> failure)
    {
        if (failure.IsSuccess)
            throw new ArgumentException("Chỉ ánh xạ lần gọi thất bại.", nameof(failure));

        var log = $"TADSS override thất bại: {failure.FailureKind}, HTTP {failure.HttpStatus?.ToString() ?? "-"}, " +
                  $"code={failure.ErrorCode ?? "-"}, CorrelationId={failure.CorrelationId}";

        return failure.FailureKind switch
        {
            TadssFailureKind.Timeout or TadssFailureKind.Network => Unavailable(LogLevel.Warning, log),
            // Hay gặp nhất là proxy trả trang HTML: yêu cầu chưa tới TADSS nên Quản lý thử lại là đúng
            TadssFailureKind.InvalidResponse => Unavailable(LogLevel.Error, log),
            _ => FromHttp(failure.HttpStatus, failure.ErrorCode, failure.ErrorFields, log)
        };
    }

    private static OverrideFailure FromHttp(int? status, string? code, IReadOnlyList<string> fields, string log) => status switch
    {
        >= 500 or 429 => Unavailable(LogLevel.Warning, log),
        // TADSS không tự trả 403, và 404 của TADSS luôn kèm mã: hai lỗi này do cấu hình hoặc đường đi (khóa sai, URL proxy đổi)
        401 or 403 => Unavailable(LogLevel.Error, log),
        404 when code == "DECISION_NOT_FOUND" => Conflict(ErrorCodes.ApprovalDecisionNotFound, log),
        404 => Unavailable(LogLevel.Error, log),
        409 => code switch
        {
            "NOT_PENDING_APPROVAL" => Conflict(ErrorCodes.BookingNotPendingApproval, log),
            "APPROVAL_EXPIRED" => Conflict(ErrorCodes.ApprovalExpired, log),
            "ALREADY_OVERRIDDEN" or "IDEMPOTENCY_KEY_REUSE" =>
                new OverrideFailure(ApprovalConflictResolver.Conflict(ErrorCodes.ApprovalAlreadyResolved), true, LogLevel.Warning, log),
            _ => Internal(log)
        },
        400 when code == "VALIDATION_ERROR" && fields.Any(DepositFields.Contains) => new OverrideFailure(
            new AppException(400, ValidationMessage, ErrorCodes.ValidationError,
                new Dictionary<string, string[]> { ["DepositRate"] = [DepositApprovalRules.InvalidRateMessage] }),
            false, LogLevel.Error, log),
        // 400 khác, 413, 415: BE gửi sai, Quản lý không sửa được
        _ => Internal(log)
    };

    private static OverrideFailure Unavailable(LogLevel level, string log) =>
        new(new AppException(503, UnavailableMessage, ErrorCodes.TadssUnavailable), false, level, log);

    private static OverrideFailure Conflict(string errorCode, string log) =>
        new(ApprovalConflictResolver.Conflict(errorCode), false, LogLevel.Warning, log);

    private static OverrideFailure Internal(string log) =>
        new(new AppException(500, InternalMessage, ErrorCodes.InternalError), false, LogLevel.Error, log);
}
