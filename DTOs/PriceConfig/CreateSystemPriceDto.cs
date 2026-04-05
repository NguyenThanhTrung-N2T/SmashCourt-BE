using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CreateSystemPriceDto
    {
        [Required(ErrorMessage = "Loại sân không được để trống")]
        public Guid CourtTypeId { get; set; }

        [Required(ErrorMessage = "Ngày hiệu lực không được để trống")]
        public DateOnly EffectiveFrom { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Phải có ít nhất 1 cấu hình giá")]
        public List<SlotPriceDto> Prices { get; set; } = [];
    }

    public class SlotPriceDto
    {
        [Required(ErrorMessage = "Thời gian bắt đầu không được để trống")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "Thời gian kết thúc không được để trống")]
        public TimeOnly EndTime { get; set; }

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Giá ngày thường phải lớn hơn 0")]
        public decimal WeekdayPrice { get; set; }

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Giá cuối tuần phải lớn hơn 0")]
        public decimal WeekendPrice { get; set; }
    }

}
