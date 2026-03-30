using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Branch
{
    public class AddServiceToBranchDto
    {
        [Required(ErrorMessage = "Vui lòng chọn dịch vụ")]
        public Guid ServiceId { get; set; }

        // Null = dùng giá mặc định từ system
        [Range(1, double.MaxValue, ErrorMessage = "Giá phải lớn hơn 0")]
        public decimal? Price { get; set; }
    }
}
