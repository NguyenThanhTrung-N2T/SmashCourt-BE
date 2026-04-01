using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Court
{
    public class UpdateCourtDto
    {
        [Required(ErrorMessage = "Tên sân không được để trống")]
        [MaxLength(100, ErrorMessage = "Tên sân tối đa 100 ký tự")]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }
        public string? AvatarUrl { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn loại sân")]
        public Guid CourtTypeId { get; set; }
    }
}
