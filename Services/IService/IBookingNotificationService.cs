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
    }
}
