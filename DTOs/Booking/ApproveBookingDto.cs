using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Body của <c>POST /api/bookings/{bookingId}/approve</c>. FE gửi tỷ lệ = phần trăm / 100.
    /// </summary>
    public class ApproveBookingDto : IValidatableObject
    {
        /// <summary>Tỷ lệ cọc trong [0, 1], tối đa 4 chữ số thập phân; 0 = miễn cọc, đơn chuyển CONFIRMED</summary>
        public decimal? DepositRate { get; set; }

        /// <summary>Lý do của Quản lý, 5..500 ký tự (code point); lưu vết ở TADSS, không gửi cho khách</summary>
        public string? Reason { get; set; }

        // Kiểm cả hai trường trong cùng một chỗ (không dùng attribute) để lỗi của hai trường luôn được báo cùng lúc
        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (!DepositApprovalRules.IsValidRate(DepositRate))
                yield return new ValidationResult(DepositApprovalRules.InvalidRateMessage, [nameof(DepositRate)]);

            if (!DepositApprovalRules.IsValidReason(Reason))
                yield return new ValidationResult(DepositApprovalRules.InvalidReasonMessage, [nameof(Reason)]);
        }
    }
}
