using System.ComponentModel.DataAnnotations;
namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class DeleteBranchPriceDto
    {
        [Required(ErrorMessage = "Loại sân không được để trống")]
        public Guid CourtTypeId { get; set; }

        [Required(ErrorMessage = "Thời gian bắt đầu không được để trống")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "Thời gian kết thúc không được để trống")]
        public TimeOnly EndTime { get; set; }

        [Required(ErrorMessage = "Ngày hiệu lực không được để trống")]
        public DateOnly EffectiveFrom { get; set; }
    }
}
