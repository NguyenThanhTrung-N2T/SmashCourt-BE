using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.PriceConfig
{
    public class CalculatePriceDto
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

            var today = GetVnToday();
            if (BookingDate < today)
                yield return new ValidationResult(
                    "Không thể đặt sân cho ngày trong quá khứ",
                    new[] { nameof(BookingDate) });
        }

        private static DateOnly GetVnToday()
        {
            var vnTimezone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(DateTime.UtcNow, vnTimezone));
        }
    }
}
