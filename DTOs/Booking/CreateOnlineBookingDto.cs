using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class CreateOnlineBookingDto : IValidatableObject
    {
        [Required(ErrorMessage = "Vui lòng chọn sân")]
        public Guid CourtId { get; set; }

        [Required(ErrorMessage = "Ngày đặt không được để trống")]
        public DateOnly BookingDate { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        public TimeOnly EndTime { get; set; }

        // Null nếu không dùng promotion
        public Guid? PromotionId { get; set; }

        // Thông tin khách vãng lai — bắt buộc nếu chưa đăng nhập
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public string? GuestEmail { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (StartTime >= EndTime)
                yield return new ValidationResult(
                    "Giờ bắt đầu phải nhỏ hơn giờ kết thúc",
                    new[] { nameof(StartTime), nameof(EndTime) });

            var today = DateTimeHelper.GetTodayInVietnam();
            if (BookingDate < today)
                yield return new ValidationResult(
                    "Không thể đặt sân cho ngày trong quá khứ",
                    new[] { nameof(BookingDate) });
        }
    }
}
