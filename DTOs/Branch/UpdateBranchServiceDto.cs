using SmashCourt_BE.Helpers;
using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Branch
{
    public class UpdateBranchServiceDto : IValidatableObject
    {
        [Required(ErrorMessage = "Giá không được để trống")]
        [Range(1, double.MaxValue, ErrorMessage = "Giá phải lớn hơn 0")]
        public decimal Price { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (!MoneyHelper.IsWholeVnd(Price))
                yield return new ValidationResult("Giá phải là số đồng nguyên", new[] { nameof(Price) });
        }
    }
}
