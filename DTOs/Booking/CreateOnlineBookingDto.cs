using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    public class CreateOnlineBookingDto : IValidatableObject
    {
        [Required(ErrorMessage = "Ngày đặt không được để trống")]
        public DateTime BookingDate { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Phải chọn ít nhất 1 sân")]
        public List<CourtSlotDto> Courts { get; set; } = [];

        // Null nếu không dùng promotion
        public Guid? PromotionId { get; set; }

        // Thông tin khách vãng lai — bắt buộc nếu chưa đăng nhập
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public string? GuestEmail { get; set; }

        // Ghi chú từ khách hàng (yêu cầu đặc biệt, ghi chú bổ sung)
        public string? Note { get; set; }

        /// <summary>
        /// [DEPRECATED - Hệ thống tự động đăng ký]
        /// Trường này không còn được sử dụng. Hệ thống sẽ TỰ ĐỘNG đăng ký nhận thông báo 
        /// khi slot trống lại nếu đặt sân thất bại do bận và khách hàng có email hợp lệ.
        /// 
        /// Logic mới:
        /// - Nếu booking thất bại do slot unavailable
        /// - VÀ khách hàng có email (GuestEmail hoặc Customer.Email)
        /// - HỆ THỐNG TỰ ĐỘNG lưu SlotInterest và gửi email khi sân trống
        /// 
        /// Giữ lại field này để backward compatibility với FE cũ.
        /// </summary>
        public bool NotifyIfUnavailable { get; set; } = false;

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            var today = DateTimeHelper.GetTodayInVietnam();
            if (DateOnly.FromDateTime(BookingDate) < today)
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

            // kiểm tra trùng lặp hoàn toàn trong cùng 1 lần đặt
            var exactDuplicates = Courts
                .GroupBy(c => new { c.CourtId, c.StartTime, c.EndTime })
                .Where(g => g.Count() > 1);

            if (exactDuplicates.Any())
                yield return new ValidationResult(
                    "Không thể đặt các slot trùng lặp hoàn toàn trong cùng 1 lần",
                    new[] { nameof(Courts) });

            // Ràng buộc 1: Tất cả sân phải có cùng khung giờ (StartTime, EndTime)
            var distinctTimeSlots = Courts
                .Select(c => new { c.StartTime, c.EndTime })
                .Distinct()
                .Count();

            if (distinctTimeSlots > 1)
                yield return new ValidationResult(
                    "Tất cả các sân trong cùng một đơn đặt phải dùng chung một mốc giờ (StartTime và EndTime)",
                    new[] { nameof(Courts) });
        }
    }
}
