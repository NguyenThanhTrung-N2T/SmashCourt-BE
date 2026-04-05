using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CreateTimeSlotDto
    {
        [Required(ErrorMessage = "Thời gian bắt đầu không được để trống")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "Thời gian kết thúc không được để trống")]
        public TimeOnly EndTime { get; set; }
    }

}
