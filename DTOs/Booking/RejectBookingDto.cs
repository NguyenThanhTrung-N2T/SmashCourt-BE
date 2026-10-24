using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Body của <c>POST /api/bookings/{bookingId}/reject</c>.
    /// </summary>
    public class RejectBookingDto : IValidatableObject
    {
        /// <summary>Lý do của Quản lý, 5..500 ký tự (code point); lưu vết ở TADSS, không gửi cho khách</summary>
        public string? Reason { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (!DepositApprovalRules.IsValidReason(Reason))
                yield return new ValidationResult(DepositApprovalRules.InvalidReasonMessage, [nameof(Reason)]);
        }
    }
}
