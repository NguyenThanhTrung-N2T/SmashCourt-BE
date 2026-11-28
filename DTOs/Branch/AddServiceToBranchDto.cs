using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Branch
{
    public class AddServiceToBranchDto : IValidatableObject
    {
        [Required(ErrorMessage = "Vui lòng chọn dịch vụ")]
        public Guid ServiceId { get; set; }

        // Null = dùng giá mặc định từ system
        [Range(1, double.MaxValue, ErrorMessage = "Giá phải lớn hơn 0")]
        public decimal? Price { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (Price is { } price && !MoneyHelper.IsWholeVnd(price))
                yield return new ValidationResult("Giá phải là số đồng nguyên", new[] { nameof(Price) });
        }
    }
}
