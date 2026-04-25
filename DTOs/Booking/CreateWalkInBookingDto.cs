using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class CreateWalkInBookingDto : IValidatableObject
    {
        [Required(ErrorMessage = "Ngày đặt không được để trống")]
        public DateTime BookingDate { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Phải chọn ít nhất 1 sân")]
        public List<CourtSlotDto> Courts { get; set; } = [];

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
            var today = DateTimeHelper.GetTodayInVietnam();
            var bookingDateOnly = DateOnly.FromDateTime(BookingDate);
            
            if (bookingDateOnly < today)
                yield return new ValidationResult(
                    "Không thể đặt sân cho ngày trong quá khứ",
                    new[] { nameof(BookingDate) });

            foreach (var court in Courts)
            {
                if (court.StartTime >= court.EndTime)
                    yield return new ValidationResult(
                        $"Giờ bắt đầu phải nhỏ hơn giờ kết thúc",
                        new[] { nameof(Courts) });
            }

            var exactDuplicates = Courts
                .GroupBy(c => new { c.CourtId, c.StartTime, c.EndTime })
                .Where(g => g.Count() > 1);

            if (exactDuplicates.Any())
                yield return new ValidationResult(
                    "Không thể đặt các slot trùng lặp hoàn toàn trong cùng 1 lần",
                    new[] { nameof(Courts) });

            var distinctTimeSlots = Courts
                .Select(c => new { c.StartTime, c.EndTime })
                .Distinct()
                .Count();

            if (distinctTimeSlots > 1)
                yield return new ValidationResult(
                    "Tất cả các sân trong cùng một đơn đặt phải dùng chung một mốc giờ (StartTime và EndTime)",
                    new[] { nameof(Courts) });

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
