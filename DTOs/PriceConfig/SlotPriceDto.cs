using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class SlotPriceDto
    {
        [Required(ErrorMessage = "Thời gian bắt đầu không được để trống")]
        public TimeSpan StartTime { get; set; }

        [Required(ErrorMessage = "Thời gian kết thúc không được để trống")]
        public TimeSpan EndTime { get; set; }

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Giá ngày thường phải lớn hơn 0")]
        public decimal WeekdayPrice { get; set; }

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Giá cuối tuần phải lớn hơn 0")]
        public decimal WeekendPrice { get; set; }
    }
}
