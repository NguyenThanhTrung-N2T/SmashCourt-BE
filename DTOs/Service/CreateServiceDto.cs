using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Service
{
    public class CreateServiceDto
    {
        [Required(ErrorMessage = "Tên dịch vụ bắt buộc")]
        [StringLength(255, ErrorMessage = "Tên dịch vụ max 255 ký tự")]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        [Required(ErrorMessage = "Đơn vị tính bắt buộc")]
        [StringLength(50, ErrorMessage = "Đơn vị tính max 50 ký tự")]
        public string Unit { get; set; } = null!;

        public decimal? DefaultPrice { get; set; }
    }
}
