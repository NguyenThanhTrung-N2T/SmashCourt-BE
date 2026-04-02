using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.CourtType
{
    public class CreateCourtTypeDto
    {
        [Required(ErrorMessage = "Tên loại sân không được để trống")]
        [MaxLength(255, ErrorMessage = "Tên loại sân tối đa 255 ký tự")]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }
    }
}
