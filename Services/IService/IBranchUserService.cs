using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchUserService
    {
        // tìm kiếm users để assign vào branch
        Task<PagedResult<UserSearchResultDto>> SearchUsersAsync(UserSearchQuery query);

        // lấy danh sách assignments của user
        Task<List<UserBranchAssignmentDto>> GetUserAssignmentsAsync(Guid userId);
    }
}
