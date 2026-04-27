using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.BranchManagement;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IUserBranchRepository
    {
        // lấy thông tin gán chi nhánh của user đang hoạt động
        Task<UserBranch?> GetActiveByUserIdAsync(Guid userId);

        // lấy thông tin quản lý chi nhánh đang hoạt động theo branchId
        Task<UserBranch?> GetActiveManagerByBranchIdAsync(Guid branchId);

        // lấy thông tin quản lý chi nhánh đang hoạt động theo branchId (bao gồm User navigation)
        Task<UserBranch?> GetActiveManagerWithUserAsync(Guid branchId);

        // tạo mới gán chi nhánh cho user
        Task<UserBranch> CreateAsync(UserBranch userBranch);

        // cập nhật gán chi nhánh cho user
        Task UpdateAsync(UserBranch userBranch);

        // kiểm tra xem user có đang gán chi nhánh nào không
        Task<bool> IsUserInBranchAsync(Guid userId, Guid branchId);

        // lấy assignment MANAGER active của user (dùng để check conflict khi assign manager mới)
        Task<UserBranch?> GetActiveManagerAssignmentByUserIdAsync(Guid userId);

        // kiểm tra user có assignment active nào khác không (dùng để check trước khi downgrade role)
        Task<bool> HasOtherActiveAssignmentsAsync(Guid userId, Guid excludeAssignmentId);

        // lấy danh sách staff của chi nhánh với filter và phân trang
        Task<PagedResult<UserBranch>> GetStaffByBranchAsync(Guid branchId, StaffFilterQuery query);

        // lấy staff assignment cụ thể
        Task<UserBranch?> GetStaffAssignmentAsync(Guid userId, Guid branchId);

        // kiểm tra user đã được assign vào chi nhánh chưa (bất kỳ role nào)
        Task<UserBranch?> GetActiveAssignmentAsync(Guid userId, Guid branchId);

        // lấy danh sách assignments của user (tất cả branches)
        Task<List<UserBranch>> GetUserAssignmentsAsync(Guid userId);
    }
}
