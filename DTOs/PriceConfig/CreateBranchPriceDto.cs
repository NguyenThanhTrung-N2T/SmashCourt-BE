using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CreateBranchPriceDto
    {
        [Required(ErrorMessage = "Loại sân không được để trống")]
        public Guid CourtTypeId { get; set; }

        [Required(ErrorMessage = "Ngày hiệu lực không được để trống")]
        public DateTime EffectiveFrom { get; set; }

        [Required(ErrorMessage = "Phải có ít nhất 1 cấu hình giá")]
        [MinLength(1, ErrorMessage = "Phải có ít nhất 1 cấu hình giá")]
        public List<SlotPriceDto> Prices { get; set; } = [];
    }
}
