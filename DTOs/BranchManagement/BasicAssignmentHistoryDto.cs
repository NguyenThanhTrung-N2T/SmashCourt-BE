using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    /// <summary>
    /// Simplified assignment history using existing UserBranch fields only
    /// </summary>
    public class BasicAssignmentHistoryDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; } = null!;
        public string UserEmail { get; set; } = null!;
        public Guid BranchId { get; set; }
        public string BranchName { get; set; } = null!;
        public UserBranchRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime AssignedAt { get; set; }
        public DateTime? EndedAt { get; set; }
    }

    public class BasicAssignmentHistoryQuery : PaginationQuery
    {
        public Guid? UserId { get; set; }
        public Guid? BranchId { get; set; }
        public UserBranchRole? Role { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? SearchTerm { get; set; }
    }
}