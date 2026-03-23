using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.LoyaltyTier
{
    // DTOs/LoyaltyTier/UpdateLoyaltyTierDto.cs
    public class UpdateLoyaltyTierDto
    {

        [Required(ErrorMessage = "Điểm tối thiểu không được để trống")]
        [Range(0, int.MaxValue, ErrorMessage = "Điểm tối thiểu phải >= 0")]
        public int MinPoints { get; set; }

        [Required(ErrorMessage = "Tỷ lệ giảm giá không được để trống")]
        [Range(0, 100, ErrorMessage = "Tỷ lệ giảm giá phải từ 0 đến 100")]
        public decimal DiscountRate { get; set; }
    }
}
