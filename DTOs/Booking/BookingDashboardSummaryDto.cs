namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Query parameters để lấy thống kê dashboard booking.
    /// </summary>
    public class BookingDashboardSummaryQuery
    {
        /// <summary>
        /// ID chi nhánh (optional).
        /// - OWNER: Có thể truyền BranchId bất kỳ hoặc null để xem tất cả
        /// - MANAGER/STAFF: BranchId sẽ bị override thành branch của mình
        /// </summary>
        public Guid? BranchId { get; set; }
    }

    /// <summary>
    /// DTO thống kê tổng quan booking hôm nay.
    /// Dùng để hiển thị dashboard cho Staff/Manager.
    /// </summary>
    public class BookingDashboardSummaryDto
    {
        /// <summary>
        /// Tổng số booking hôm nay (tất cả status).
        /// </summary>
        public int TodayBookings { get; set; }

        /// <summary>
        /// Số booking đang chơi (status = IN_PROGRESS).
        /// </summary>
        public int ActiveBookings { get; set; }

        /// <summary>
        /// Số booking đã hoàn thành (status = COMPLETED).
        /// </summary>
        public int CompletedBookings { get; set; }

        /// <summary>
        /// Số booking đã hủy (status = CANCELLED, CANCELLED_PENDING_REFUND, CANCELLED_REFUNDED).
        /// </summary>
        public int CancelledBookings { get; set; }

        /// <summary>
        /// Doanh thu hôm nay (VNĐ).
        /// Chỉ tính booking đã thanh toán (PaymentStatus = PAID) và không bị hủy.
        /// </summary>
        public decimal TodayRevenue { get; set; }

        /// <summary>
        /// Số đơn chờ hoàn tiền (RefundStatus = PENDING).
        /// Tính tất cả đơn chờ hoàn tiền, không chỉ hôm nay.
        /// </summary>
        public int PendingRefunds { get; set; }
    }
}
