using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class CreateWalkInBookingDto : IValidatableObject
    {
        [Required(ErrorMessage = "Vui lòng chọn sân")]
        public Guid CourtId { get; set; }

        [Required(ErrorMessage = "Ngày đặt không được để trống")]
        public DateOnly BookingDate { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        public TimeOnly StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        public TimeOnly EndTime { get; set; }

        // Null nếu không tìm được tài khoản khách
        public Guid? CustomerId { get; set; }

        // Thông tin khách vãng lai
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public string? GuestEmail { get; set; }

        // Promotion — chỉ áp dụng khi có CustomerId
        public Guid? PromotionId { get; set; }

        public string? Note { get; set; }

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
            // Phải có CustomerId hoặc thông tin khách vãng lai
            if (CustomerId == null &&
                (string.IsNullOrEmpty(GuestName) || string.IsNullOrEmpty(GuestPhone)))
                yield return new ValidationResult(
                    "Vui lòng nhập thông tin khách hoặc chọn tài khoản khách hàng",
                    new[] { nameof(GuestName) });

            // Promotion chỉ dùng được khi có tài khoản
            if (PromotionId.HasValue && CustomerId == null)
                yield return new ValidationResult(
                    "Khách vãng lai không được áp dụng khuyến mãi",
                    new[] { nameof(PromotionId) });
        }
    }
}
