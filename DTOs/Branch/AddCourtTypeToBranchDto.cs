using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Branch
{
    public class AddCourtTypeToBranchDto
    {
        [Required(ErrorMessage = "Vui lòng chọn loại sân")]
        public Guid CourtTypeId { get; set; }
    }
}
