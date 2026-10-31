namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Một dòng của hàng chờ duyệt cọc (chỉ nhân viên xem, nên có lý do cần duyệt); không có trường nội bộ của TADSS.
    /// </summary>
    public class PendingApprovalBookingDto
    {
        public Guid BookingId { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = string.Empty;
        /// <summary>Luôn có vì đặt online bắt buộc đăng nhập; nullable để tương thích</summary>
        public Guid? CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerPhone { get; set; }
        /// <summary>Luôn false vì đặt online bắt buộc đăng nhập; giữ để tương thích</summary>
        public bool IsGuest { get; set; }
        /// <summary>Tên hạng thành viên trong SmashCourt (không phải hạng đã ánh xạ của TADSS)</summary>
        public string? LoyaltyTierName { get; set; }
        /// <summary>Ngày chơi, ra JSON <c>yyyy-MM-dd</c></summary>
        public DateOnly BookingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public List<PendingApprovalCourtDto> Courts { get; set; } = [];
        /// <summary>Tổng sau giảm giá, cơ sở tính cọc (totalPrice đã gửi TADSS)</summary>
        public decimal FinalTotal { get; set; }
        public string? ApprovalReasonCode { get; set; }
        public List<string> ApprovalReasons { get; set; } = [];
        public DateTime ApprovalExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? Note { get; set; }
    }
}
