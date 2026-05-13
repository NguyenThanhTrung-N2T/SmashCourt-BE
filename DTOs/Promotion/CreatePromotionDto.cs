using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.Promotion
{
    public class CreatePromotionDto
    {
        [Required(ErrorMessage = "Tên khuyến mãi không được để trống")]
        [MaxLength(255, ErrorMessage = "Tên khuyến mãi tối đa 255 ký tự")]
        public string Name { get; set; } = null!;

        [MaxLength(50, ErrorMessage = "Mã khuyến mãi tối đa 50 ký tự")]
        public string? Code { get; set; }

        public string? PromoDisplayUrl { get; set; }

        public string? Description { get; set; }

        [Required(ErrorMessage = "Loại giảm giá không được để trống")]
        public DiscountTypeEnum DiscountType { get; set; }

        [Required(ErrorMessage = "Giá trị giảm giá không được để trống")]
        [Range(0.01, 99999999.99, ErrorMessage = "Giá trị giảm giá phải từ 0.01 đến 9,999,999,999.99")]
        public decimal DiscountValue { get; set; }

        [Range(0.01, 99999999.99, ErrorMessage = "Giá trị giảm tối đa phải từ 0.01 đến 9,999,999,999.99")]
        public decimal? MaxDiscountAmount { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Giới hạn sử dụng phải lớn hơn 0")]
        public int? UsageLimit { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Giới hạn sử dụng mỗi người phải lớn hơn 0")]
        public int? UsagePerUserLimit { get; set; }

        [Required(ErrorMessage = "Ngày bắt đầu không được để trống")]
        public DateTime StartDate { get; set; }

        [Required(ErrorMessage = "Ngày kết thúc không được để trống")]
        public DateTime EndDate { get; set; }

        public List<PromotionConditionDto>? Conditions { get; set; }
    }

    public class PromotionConditionDto
    {
        [Required(ErrorMessage = "Loại điều kiện không được để trống")]
        [MaxLength(50, ErrorMessage = "Loại điều kiện tối đa 50 ký tự")]
        public string ConditionType { get; set; } = null!;

        [Required(ErrorMessage = "Giá trị điều kiện không được để trống")]
        public string ConditionValue { get; set; } = null!;
    }
}
