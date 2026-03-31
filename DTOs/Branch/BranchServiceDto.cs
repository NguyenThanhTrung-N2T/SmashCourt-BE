using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Branch
{
    public class BranchServiceDto
    {
        public Guid Id { get; set; }
        public Guid ServiceId { get; set; }
        public string ServiceName { get; set; } = null!;
        public string? Description { get; set; }
        public string Unit { get; set; } = null!;
        public decimal DefaultPrice { get; set; }   // Giá mặc định từ system
        public decimal EffectivePrice { get; set; } // Giá thực tế áp dụng tại chi nhánh
        public BranchServiceStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
