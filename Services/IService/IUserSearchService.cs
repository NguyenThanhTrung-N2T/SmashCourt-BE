using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;

namespace SmashCourt_BE.Services.IService
{
    public interface IUserSearchService
    {
        /// <summary>
        /// Search users for branch assignment with comprehensive filtering
        /// </summary>
        /// <param name="query">Search query with filters and pagination</param>
        /// <returns>Paginated list of users with eligibility information</returns>
        Task<PagedResult<UserSearchResultDto>> SearchUsersAsync(UserSearchQuery query);

        /// <summary>
        /// Get all branch assignments for a specific user
        /// </summary>
        /// <param name="userId">User ID to get assignments for</param>
        /// <returns>List of user's branch assignments</returns>
        Task<List<UserBranchAssignmentDto>> GetUserAssignmentsAsync(Guid userId);
    }
}