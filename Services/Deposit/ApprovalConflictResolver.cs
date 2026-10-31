using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.Deposit;

/// <summary>
/// Chọn lỗi 409 khi đơn không duyệt / từ chối được, theo trạng thái trong DB của BE. Hàm thuần.
/// </summary>
public static class ApprovalConflictResolver
{
    public const string AlreadyResolvedMessage = "Đơn này đã được xử lý trước đó. Vui lòng tải lại hàng chờ.";
    public const string ExpiredMessage = "Đơn đã hết hạn duyệt và sẽ được hệ thống hủy tự động.";
    public const string NotPendingMessage = "Đơn không còn ở trạng thái chờ duyệt. Vui lòng tải lại hàng chờ.";
    public const string DecisionNotFoundMessage =
        "Không tìm thấy quyết định cọc của đơn; đơn sẽ tự hủy khi hết hạn duyệt, vui lòng báo quản trị.";

    // Null = được gọi TADSS. Thứ tự ưu tiên: đã xử lý ⇒ hết hạn duyệt ⇒ không chờ duyệt ⇒ thiếu dữ liệu quyết định
    public static AppException? Resolve(Booking booking, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Mốc thời gian phải có Kind = Utc.", nameof(nowUtc));

        // Gồm cả đơn đã duyệt rồi bị hủy vì hết hạn thanh toán
        if (booking.ApprovalResolvedAt.HasValue)
            return Conflict(ErrorCodes.ApprovalAlreadyResolved);

        var pending = booking.Status == BookingStatus.PENDING_APPROVAL;
        if (booking.CancelReason == BookingCancelReason.APPROVAL_EXPIRED
            || (pending && booking.ApprovalExpiresAt <= nowUtc))
            return Conflict(ErrorCodes.ApprovalExpired);

        if (!pending)
            return Conflict(ErrorCodes.BookingNotPendingApproval);

        // DB thật cấm trạng thái này; không biết quyết định hay hạn thì không gọi TADSS
        if (booking.TadssDecisionId is null || booking.ApprovalExpiresAt is null)
            return Conflict(ErrorCodes.ApprovalDecisionNotFound);

        return null;
    }

    // Lỗi 409 kèm câu thông báo chuẩn của từng mã
    public static AppException Conflict(string errorCode) => new(409, errorCode switch
    {
        ErrorCodes.ApprovalAlreadyResolved => AlreadyResolvedMessage,
        ErrorCodes.ApprovalExpired => ExpiredMessage,
        ErrorCodes.BookingNotPendingApproval => NotPendingMessage,
        ErrorCodes.ApprovalDecisionNotFound => DecisionNotFoundMessage,
        _ => throw new ArgumentOutOfRangeException(nameof(errorCode), errorCode, "Không phải mã 409 của luồng duyệt.")
    }, errorCode);
}
