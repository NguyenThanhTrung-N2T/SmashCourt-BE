namespace SmashCourt_BE.Jobs.Interfaces
{
    public interface IBookingJob
    {
        // Hủy booking hết hạn thanh toán
        Task CancelExpiredPendingBookingsAsync();

        // Xử lý booking hết giờ đã đặt
        Task ProcessExpiredActiveBookingsAsync();

        // Xóa slot lock hết hạn
        Task CleanupExpiredSlotLocksAsync();

        // Phát hiện và đánh dấu NO_SHOW
        Task DetectNoShowBookingsAsync();
    }
}
