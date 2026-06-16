using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using System.Threading.Tasks;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBranchServiceRepository
    {
        Task<PagedResult<BranchService>> GetByBranchAsync(Guid branchId, int page = 1, int pageSize = 10);

        // Lấy thông tin chi tiết của một dịch vụ trong chi nhánh theo branchId và serviceId
        Task<BranchService?> GetByBranchServiceAsync(Guid branchId, Guid serviceId);


        Task<BranchService> CreateAsync(BranchService branchService);
        Task<BranchService?> UpdateAsync(BranchService branchService);
        Task<bool> SoftDeleteAsync(Guid branchId, Guid serviceId);
    }
}

