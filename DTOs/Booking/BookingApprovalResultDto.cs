namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Response của <c>/approve</c> và <c>/reject</c>. Mức cọc là số TADSS trả về; từ chối thì các trường cọc null.
    /// </summary>
    public class BookingApprovalResultDto
    {
        public Guid BookingId { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        /// <summary><c>APPROVED</c> | <c>REJECTED</c></summary>
        public string ApprovalResult { get; set; } = string.Empty;
        /// <summary><c>PENDING</c> (duyệt cọc &gt; 0), <c>CONFIRMED</c> (duyệt 0 %), <c>CANCELLED</c> (từ chối)</summary>
        public string Status { get; set; } = string.Empty;
        public decimal FinalTotal { get; set; }
        public decimal? DepositRate { get; set; }
        public decimal? DepositAmount { get; set; }
        public decimal? RemainingAmount { get; set; }
        public string? DepositSource { get; set; }
        /// <summary>Hạn thanh toán cọc (cũng là hạn giữ chỗ tuyệt đối); null khi duyệt 0 % hoặc từ chối</summary>
        public DateTime? ExpiresAt { get; set; }
        public Guid? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}
