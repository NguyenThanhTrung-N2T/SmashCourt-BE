using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.DTOs.Branch;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBranchRepository
    {
        // lấy danh sách chi nhánh có phân trang, có thể bao gồm cả chi nhánh bị đình chỉ hoạt động
        Task<PagedResult<Branch>> GetAllAsync(int page, int pageSize, bool includeSupended);

        // lấy chi nhánh theo id, trả về null nếu không tìm thấy
        Task<Branch?> GetByIdAsync(Guid id);

        // kiểm tra xem đã tồn tại chi nhánh nào có tên giống với tên được cung cấp hay chưa, có thể loại trừ một chi nhánh theo id (dùng khi cập nhật để tránh so sánh với chính nó)
        Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null);

        // tạo chi nhánh mới
        Task<Branch> CreateAsync(Branch branch);

        // tạo chi nhánh mới đồng thời gán manager cho chi nhánh đó
        Task<Branch> CreateWithManagerAsync(Branch branch, UserBranch userBranch);

        // cập nhật thông tin chi nhánh
        Task UpdateAsync(Branch branch);

        // kiểm tra xem chi nhánh có booking nào đang hoạt động hay không
        Task<bool> HasActiveBookingsAsync(Guid branchId);

        // lấy chi nhánh cùng với thông tin assignment của manager (nếu có)
        Task<(Branch Branch, UserBranch? ManagerAssignment)?> GetWithManagerAsync(Guid id);

        // lấy danh sách loại sân của chi nhánh
        Task<List<BranchCourtType>> GetCourtTypesAsync(Guid branchId);

        // lấy danh sách TẤT CẢ loại sân trong hệ thống, kèm theo trạng thái và số lượng sân tại chi nhánh
        Task<List<BranchCourtTypeDto>> GetAllCourtTypeDetailsAsync(Guid branchId);

        // lấy thông tin loại sân của chi nhánh theo id, trả về null nếu không tìm thấy
        Task<BranchCourtType?> GetBranchCourtTypeAsync(Guid branchId, Guid courtTypeId);

        // thêm loại sân cho chi nhánh
        Task<BranchCourtType> AddCourtTypeAsync(BranchCourtType branchCourtType);

        // cập nhật thông tin loại sân của chi nhánh
        Task UpdateBranchCourtTypeAsync(BranchCourtType branchCourtType);

        // kiểm tra chi nhánh có loại sân nào không
        Task<bool> HasCourtsWithTypeAsync(Guid branchId, Guid courtTypeId);

        // lấy danh sách dịch vụ của chi nhánh
        Task<List<BranchService>> GetServicesAsync(Guid branchId);

        // lấy thông tin dịch vụ của chi nhánh theo id, trả về null nếu không tìm thấy
        Task<BranchService?> GetBranchServiceAsync(Guid branchId, Guid serviceId);

        // thêm dịch vụ cho chi nhánh
        Task<BranchService> AddServiceAsync(BranchService branchService);

        // cập nhật thông tin dịch vụ của chi nhánh
        Task UpdateBranchServiceAsync(Guid id, decimal price, BranchServiceStatus status);

        // kiểm tra loại sân có đang dùng cho chi nhánh hay không
        Task<bool> IsCourtTypeEnabledAsync(Guid branchId, Guid courtTypeId);

    }
}
