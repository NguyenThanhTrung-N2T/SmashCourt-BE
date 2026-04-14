using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CalculatePriceDto : IValidatableObject
    {
        [Required(ErrorMessage = "Sân không được để trống")]
        public Guid CourtId { get; set; }

        [Required(ErrorMessage = "Ngày đặt không được để trống")]
        public DateOnly BookingDate { get; set; }

        [Required(ErrorMessage = "Thời gian bắt đầu không được để trống")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "Thời gian kết thúc không được để trống")]
        public TimeOnly EndTime { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (StartTime >= EndTime)
                yield return new ValidationResult(
                    "Thời gian bắt đầu phải nhỏ hơn thời gian kết thúc",
                    new[] { nameof(StartTime), nameof(EndTime) });

            var today = SmashCourt_BE.Helpers.DateTimeHelper.GetTodayInVietnam();
            if (BookingDate < today)
                yield return new ValidationResult(
                    "Không thể đặt sân cho ngày trong quá khứ",
                    new[] { nameof(BookingDate) });
        }
    }
}
