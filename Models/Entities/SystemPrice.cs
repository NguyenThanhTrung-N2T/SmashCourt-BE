namespace SmashCourt_BE.Models.Entities
{
    public class SystemPrice
    {
        public Guid Id { get; set; }
        public Guid CourtTypeId { get; set; }
        public Guid TimeSlotId { get; set; }
        public decimal Price { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation
        public CourtType CourtType { get; set; } = null!;
        public TimeSlot TimeSlot { get; set; } = null!;
    }
}
