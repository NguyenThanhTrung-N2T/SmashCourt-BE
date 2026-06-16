namespace SmashCourt_BE.Models.Entities
{
    /// <summary>
    /// Lưu thông tin khách hàng quan tâm đến một slot sân đang bị chiếm.
    /// Khi slot được giải phóng (booking bị hủy), hệ thống sẽ gửi email thông báo
    /// cho tất cả người đã đăng ký interest cho slot đó.
    /// </summary>
    public class SlotInterest
    {
        public Guid Id { get; set; }
        public Guid CourtId { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }

        /// <summary>Email nhận thông báo khi slot rảnh</summary>
        public string Email { get; set; } = null!;

        /// <summary>null nếu là khách vãng lai chưa đăng nhập</summary>
        public Guid? CustomerId { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Tự động hết hạn sau 24h hoặc sau ngày đặt sân.
        /// Hangfire cleanup job sẽ xóa các record hết hạn mỗi 1 giờ.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        // Navigation
        public Court Court { get; set; } = null!;
        public User? Customer { get; set; }
    }
}
