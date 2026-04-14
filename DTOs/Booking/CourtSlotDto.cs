using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class CourtSlotDto
    {
        [Required(ErrorMessage = "Vui lòng chọn sân")]
        public Guid CourtId { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        public TimeOnly EndTime { get; set; }
    }
}
