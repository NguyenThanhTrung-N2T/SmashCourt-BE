using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Service
{
    public class CreateBranchServiceDto
    {
        [Required]
        public Guid ServiceId { get; set; }

        [Required, Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        public BranchServiceStatus Status { get; set; } = BranchServiceStatus.ENABLED;
    }
}

