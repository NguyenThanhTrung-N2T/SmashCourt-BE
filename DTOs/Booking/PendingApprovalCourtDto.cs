namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>Một sân của đơn trong hàng chờ duyệt.</summary>
    public class PendingApprovalCourtDto
    {
        public Guid CourtId { get; set; }
        public string CourtName { get; set; } = string.Empty;
    }
}
