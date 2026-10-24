namespace SmashCourt_BE.DTOs.Booking
{
    // Response của POST /api/bookings/online và POST /api/me/bookings/{id}/retry-payment.
    // Khóa có giá trị null vẫn được serialize; không bao giờ chứa trường nội bộ của TADSS (decisionId, strategyName...).
    public class OnlineBookingResponse
    {
        public Guid BookingId { get; set; }
        public string BookingCode { get; set; } = null!;
        public string Status { get; set; } = null!; // PENDING | PENDING_APPROVAL | CONFIRMED — FE rẽ nhánh theo trường này
        public string? PaymentUrl { get; set; } // VNPay URL; null khi đơn không thanh toán online (chờ duyệt, CONFIRMED)
        public DateTime? ExpiresAt { get; set; } // Hạn thanh toán; null như PaymentUrl
        public decimal FinalTotal { get; set; }

        // Trường cọc (2.2); null khi PENDING_APPROVAL
        public decimal? DepositRate { get; set; }
        public decimal? DepositAmount { get; set; }
        public decimal? RemainingAmount { get; set; }
        public string? DepositSource { get; set; }
        public DateTime? ApprovalExpiresAt { get; set; } // Có khi PENDING_APPROVAL
    }
}
