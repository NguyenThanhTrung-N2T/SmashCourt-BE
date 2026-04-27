using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.BranchManagement
{
    public class StaffFilterQuery : PaginationQuery
    {
        public bool? IsActive { get; set; }
        public UserBranchRole? Role { get; set; }
        public string? SearchTerm { get; set; }
    }
}