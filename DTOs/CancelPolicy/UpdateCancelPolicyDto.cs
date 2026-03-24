using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.CancelPolicy
{
    public class UpdateCancelPolicyDto
    {
        [Required(ErrorMessage = "Vui lòng nhập số giờ")]
        [Range(0, 100, ErrorMessage = "Số giờ phải lớn hơn hoặc bằng 0")]
        public int HoursBefore { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập phần trăm hoàn tiền")]
        [Range(0, 100, ErrorMessage = "Phần trăm hoàn tiền phải từ 0 đến 100")]
        public decimal RefundPercent { get; set; }

        public string? Description { get; set; }
    }
}
