namespace SmashCourt_BE.Models.Entities
{
    public class CancelPolicy
    {
        public Guid Id { get; set; }
        public int HoursBefore { get; set; }
        public decimal RefundPercent { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
