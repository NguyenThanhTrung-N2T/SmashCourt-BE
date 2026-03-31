using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Branch
{
    public class UpdateBranchDto
    {
        [Required(ErrorMessage = "Tên chi nhánh không được để trống")]
        [MaxLength(255, ErrorMessage = "Tên chi nhánh tối đa 255 ký tự")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Địa chỉ không được để trống")]
        public string Address { get; set; } = null!;

        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        [Required(ErrorMessage = "Giờ mở cửa không được để trống")]
        public TimeOnly OpenTime { get; set; }

        [Required(ErrorMessage = "Giờ đóng cửa không được để trống")]
        public TimeOnly CloseTime { get; set; }
    }
}
