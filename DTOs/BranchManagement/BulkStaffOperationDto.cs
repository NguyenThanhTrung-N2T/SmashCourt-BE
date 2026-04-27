using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class BulkStaffOperationDto
    {
        [Required(ErrorMessage = "Operation không được để trống")]
        public BulkOperationType Operation { get; set; }

        [Required(ErrorMessage = "UserIds không được để trống")]
        [MinLength(1, ErrorMessage = "Phải có ít nhất 1 user")]
        public List<Guid> UserIds { get; set; } = [];

        public UserBranchRole? NewRole { get; set; }

        [MaxLength(500, ErrorMessage = "Lý do tối đa 500 ký tự")]
        public string? Reason { get; set; }

        [MaxLength(1000, ErrorMessage = "Ghi chú tối đa 1000 ký tự")]
        public string? Notes { get; set; }
    }

    public class BulkStaffOperationResultDto
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<BulkOperationError> Errors { get; set; } = [];
    }

    public class BulkOperationError
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; } = null!;
        public string ErrorMessage { get; set; } = null!;
    }

    public enum BulkOperationType
    {
        ADD_STAFF = 0,
        REMOVE_STAFF = 1,
        CHANGE_ROLE = 2
    }
}