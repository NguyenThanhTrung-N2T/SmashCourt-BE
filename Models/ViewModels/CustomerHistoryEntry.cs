using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — dữ liệu thô của một booking đã kết thúc hoặc đã hủy của khách, dùng để đếm lịch sử gửi TADSS.
    /// Repository chỉ đọc, <c>CustomerHistoryCalculator</c> mới phân loại.
    /// </summary>
    /// <param name="Status">Trạng thái booking (COMPLETED, NO_SHOW hoặc một trạng thái hủy).</param>
    /// <param name="CancelReason">Lý do hủy (NULL với đơn không hủy hoặc đơn hủy chưa ghi lý do).</param>
    /// <param name="CancelledAt">Thời điểm hủy (UTC).</param>
    /// <param name="LastPlayDate">Ngày của sân kết thúc muộn nhất (giờ VN); NULL nếu booking không có sân nào.</param>
    /// <param name="LastPlayEndTime">Giờ kết thúc của sân kết thúc muộn nhất (giờ VN).</param>
    /// <param name="HasSuccessfulPayment">Có Payment SUCCESS hoặc REFUNDED.</param>
    /// <param name="MaxRefundPercent">Tỷ lệ hoàn lớn nhất trong các khoản hoàn của booking; NULL khi chưa có bản ghi hoàn.</param>
    public sealed record CustomerHistoryEntry(
        BookingStatus Status,
        BookingCancelReason? CancelReason,
        DateTime? CancelledAt,
        DateOnly? LastPlayDate,
        TimeOnly? LastPlayEndTime,
        bool HasSuccessfulPayment,
        decimal? MaxRefundPercent);
}
