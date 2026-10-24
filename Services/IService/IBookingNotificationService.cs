using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingNotificationService
    {
        // Báo kết quả đặt sân online sau commit: email theo nhánh (xác nhận kèm link hủy, hoặc đang chờ duyệt) và sự kiện SignalR
        Task NotifyOnlineBookingAsync(Booking booking, DepositBranch branch, string? rawCancelToken);

        // Báo kết quả duyệt / từ chối sau commit: sự kiện SignalR, báo người chờ slot khi từ chối, email cho khách
        Task NotifyApprovalResolvedAsync(Booking booking, bool rejected, string? rawCancelToken);

        // Báo đơn vừa bị hủy sau commit: email kèm số hoàn tại chi nhánh, báo người chờ slot, BookingCancelled kèm lưới giờ;
        // đơn đang chờ duyệt thì báo rời hàng chờ
        Task NotifyCancelledAsync(Booking booking, BookingStatus previousStatus, decimal refundAmount);

        // Báo thanh toán thành công: email xác nhận kèm link hủy và phần cọc / còn lại, sự kiện PaymentSuccess
        Task NotifyPaymentSucceededAsync(Booking booking, Payment payment, string rawCancelToken);

        // Báo thanh toán thất bại: PaymentFailed kèm lưới giờ; holdUntilUtc là hạn giữ chỗ mới, null khi đơn không còn chờ thanh toán
        Task NotifyPaymentFailedAsync(Booking booking, Payment payment, DateTime? holdUntilUtc);

        // Báo tiền về sau khi đơn đã hủy: email hủy kèm số hoàn tại chi nhánh, BookingCancelled để nhân viên thấy đơn chờ hoàn
        Task NotifyLatePaymentAsync(Booking booking, decimal refundAmount);
    }
}
