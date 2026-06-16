using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class PriceSlotInput
    {
        /// <summary>
        /// Start time in HH:mm:ss format
        /// </summary>
        [Required(ErrorMessage = "Thời gian bắt đầu không được để trống")]
        public string StartTime { get; set; } = string.Empty;

        /// <summary>
        /// End time in HH:mm:ss format
        /// </summary>
        [Required(ErrorMessage = "Thời gian kết thúc không được để trống")]
        public string EndTime { get; set; } = string.Empty;

        /// <summary>
        /// Price for weekdays
        /// </summary>
        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "Giá ngày thường không được âm")]
        public decimal WeekdayPrice { get; set; }

        /// <summary>
        /// Price for weekends
        /// </summary>
        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "Giá cuối tuần không được âm")]
        public decimal WeekendPrice { get; set; }
    }
}
