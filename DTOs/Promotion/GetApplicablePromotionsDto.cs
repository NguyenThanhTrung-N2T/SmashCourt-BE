using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Promotion
{
    /// <summary>
    /// Request DTO để lấy danh sách promotion áp dụng được cho booking context cụ thể
    /// </summary>
    public class GetApplicablePromotionsDto : IValidatableObject
    {
        [Required(ErrorMessage = "Chi nhánh không được để trống")]
        public Guid BranchId { get; set; }

        [Required(ErrorMessage = "Sân không được để trống")]
        public Guid CourtId { get; set; }

        [Required(ErrorMessage = "Ngày đặt không được để trống")]
        public DateTime BookingDate { get; set; }

        [Required(ErrorMessage = "Giờ bắt đầu không được để trống")]
        public TimeSpan StartTime { get; set; }

        [Required(ErrorMessage = "Giờ kết thúc không được để trống")]
        public TimeSpan EndTime { get; set; }

        [Required(ErrorMessage = "Tổng tiền không được để trống")]
        [Range(0.01, 99999999.99, ErrorMessage = "Tổng tiền phải lớn hơn 0")]
        public decimal BookingAmount { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (StartTime >= EndTime)
                yield return new ValidationResult(
                    "Giờ bắt đầu phải nhỏ hơn giờ kết thúc",
                    new[] { nameof(StartTime), nameof(EndTime) });
        }
    }
}
