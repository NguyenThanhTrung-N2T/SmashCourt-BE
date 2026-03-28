using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Service
{
    public class CreateServiceDto
    {
        [Required(ErrorMessage = "Tên dịch vụ không được để trống")]
        [MaxLength(255, ErrorMessage = "Tên dịch vụ tối đa 255 ký tự")]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        [Required(ErrorMessage = "Đơn vị tính không được để trống")]
        [MaxLength(50, ErrorMessage = "Đơn vị tính tối đa 50 ký tự")]
        public string Unit { get; set; } = null!;

        [Required(ErrorMessage = "Giá mặc định không được để trống")]
        [Range(1, double.MaxValue, ErrorMessage = "Giá mặc định phải lớn hơn 0")]
        public decimal DefaultPrice { get; set; }
    }
}
