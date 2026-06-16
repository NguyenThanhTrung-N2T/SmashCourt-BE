using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class UserSearchQuery : PaginationQuery
    {
        public string? SearchTerm { get; set; }
        public UserRole? Role { get; set; }
        public UserStatus? Status { get; set; }
        public bool? ExcludeAssignedToBranch { get; set; }
        public Guid? ExcludeBranchId { get; set; }
        public bool? EligibleForManager { get; set; }
        public bool? EligibleForStaff { get; set; }
    }
}