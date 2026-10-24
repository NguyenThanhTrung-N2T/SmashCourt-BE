using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Services.IService
{
    public interface ISlotInterestNotificationService
    {
        // Báo email cho người đã đăng ký "báo khi trống" của các slot booking vừa nhả, rồi xóa đăng ký (một lần). Gọi SAU khi commit.
        // Booking cần nạp sẵn Branch và BookingCourts.Court. Lỗi gửi một người chỉ ghi log, không chặn người khác.
        Task NotifyReleasedSlotsAsync(Booking booking);
    }
}
