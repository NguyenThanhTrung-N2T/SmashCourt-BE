using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class AddBookingServiceDto
    {
        [Required(ErrorMessage = "Vui lòng chọn dịch vụ")]
        public Guid ServiceId { get; set; }

        [Range(1, 100, ErrorMessage = "Số lượng phải từ 1 đến 100")]
        public int Quantity { get; set; } = 1;
    }
}
