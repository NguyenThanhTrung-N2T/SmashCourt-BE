using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Dịch vụ gửi thông báo SignalR tập trung.
/// Hỗ trợ phát tín hiệu tới 4 nhóm kênh:
///   user_{userId}                          — Kênh cá nhân của khách hàng
///   role_{ROLE}                            — Kênh phân quyền hệ thống (ví dụ: role_OWNER)
///   branch_{branchId}                      — Kênh vận hành của Staff/Manager tại chi nhánh
///   timegrid_{branchId}_{courtTypeId}_{date} — Kênh công khai dành cho khách hàng đang xem lịch đặt sân
/// </summary>
public interface IBroadcastService
{
    /// <summary>
    /// Phát đi sự kiện realtime liên quan đến đơn đặt sân (Booking).
    /// </summary>
    /// <param name="eventName">Tên sự kiện SignalR lấy từ <see cref="SmashCourt_BE.Common.Constants.SignalREvents"/>.</param>
    /// <param name="notification">Nội dung payload thông báo.</param>
    /// <param name="booking">Thực thể Booking dùng để xác định các nhóm nhận tin. Cần nạp sẵn BookingCourts và Court.</param>
    /// <param name="includeTimeGrid">
    /// Nếu true, sự kiện sẽ được gửi đến cả các nhóm <c>timegrid_...</c> tương ứng.
    /// Đặt là false đối với các sự kiện không ảnh hưởng đến trạng thái trống của lịch (ví dụ: check-in).
    /// </param>
    Task BroadcastBookingEventAsync(
        string eventName,
        BookingNotificationDto notification,
        Booking booking,
        bool includeTimeGrid = false);

    /// <summary>
    /// Phát đi sự kiện realtime liên quan đến thanh toán (Payment).
    /// </summary>
    /// <param name="eventName">Tên sự kiện SignalR.</param>
    /// <param name="notification">Nội dung payload thông báo.</param>
    /// <param name="booking">Thực thể Booking. Cần nạp sẵn BookingCourts và Court.</param>
    /// <param name="includeTimeGrid">
    /// Nếu true, sự kiện sẽ được gửi đến cả các nhóm <c>timegrid_...</c>.
    /// Chỉ dùng khi kết quả thanh toán trực tiếp làm thay đổi trạng thái trống của slot sân (ví dụ: thanh toán thất bại).
    /// </param>
    Task BroadcastPaymentEventAsync(
        string eventName,
        PaymentNotificationDto notification,
        Booking booking,
        bool includeTimeGrid = false);

    /// <summary>
    /// Như <see cref="BroadcastBookingEventAsync"/> nhưng <b>không</b> gửi vào nhóm <c>user_{customerId}</c>: dùng khi khách đã nhận kết quả
    /// ngay ở response (VD đơn online bị TADSS từ chối), chỉ nhân viên chi nhánh, OWNER và lưới giờ (nếu bật) cần biết.
    /// </summary>
    Task BroadcastBookingEventToStaffAsync(
        string eventName,
        BookingNotificationDto notification,
        Booking booking,
        bool includeTimeGrid = false);

    /// <summary>
    /// Phát <c>BookingPendingApproval</c> tới <c>branch_{branchId}</c> và <c>role_OWNER</c>.
    /// </summary>
    Task BroadcastBookingPendingApprovalAsync(BookingPendingApprovalNotificationDto notification);

    /// <summary>
    /// Phát <c>PendingApprovalQueueChanged</c> tới <c>branch_{branchId}</c> và <c>role_OWNER</c>.
    /// </summary>
    Task BroadcastPendingApprovalQueueChangedAsync(PendingApprovalQueueChangedNotificationDto notification);

    /// <summary>
    /// Phát <c>BookingApprovalResolved</c> tới <c>user_{customerId}</c> (nếu khách có tài khoản), <c>branch_{branchId}</c> và
    /// <c>role_OWNER</c>. <paramref name="customerId"/> chỉ dùng để chọn nhóm, không nằm trong payload.
    /// </summary>
    Task BroadcastBookingApprovalResolvedAsync(BookingApprovalResolvedNotificationDto notification, Guid? customerId);
}
