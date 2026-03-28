using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Service
{
    public class ServiceDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Unit { get; set; } = null!;
        public decimal DefaultPrice { get; set; }
        public ServiceStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
