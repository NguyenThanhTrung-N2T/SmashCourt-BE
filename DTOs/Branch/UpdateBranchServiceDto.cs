using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Branch
{
    public class UpdateBranchServiceDto
    {
        [Required(ErrorMessage = "Giá không được để trống")]
        [Range(1, double.MaxValue, ErrorMessage = "Giá phải lớn hơn 0")]
        public decimal Price { get; set; }
    }
}
