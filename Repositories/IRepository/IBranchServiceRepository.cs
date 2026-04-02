using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using System.Threading.Tasks;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBranchServiceRepository
    {
        Task<PagedResult<BranchService>> GetByBranchAsync(Guid branchId, int page = 1, int pageSize = 10);
        Task<BranchService?> GetByBranchServiceAsync(Guid branchId, Guid serviceId);
        Task<BranchService> CreateAsync(BranchService branchService);
        Task<BranchService?> UpdateAsync(BranchService branchService);
        Task<bool> SoftDeleteAsync(Guid branchId, Guid serviceId);
    }
}

