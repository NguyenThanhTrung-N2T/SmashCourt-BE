using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Promotion
{
    public class CreatePromotionDto
    {
        [Required(ErrorMessage = "Tên khuyến mãi không được để trống")]
        [MaxLength(255, ErrorMessage = "Tên khuyến mãi tối đa 255 ký tự")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Tỷ lệ giảm giá không được để trống")]
        [Range(0.01, 100, ErrorMessage = "Tỷ lệ giảm giá phải từ 0.01 đến 100")]
        public decimal DiscountRate { get; set; }

        [Required(ErrorMessage = "Ngày bắt đầu không được để trống")]
        public DateOnly StartDate { get; set; }

        [Required(ErrorMessage = "Ngày kết thúc không được để trống")]
        public DateOnly EndDate { get; set; }
    }
}
