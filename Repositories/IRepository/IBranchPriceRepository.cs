using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBranchPriceRepository
    {
        Task<List<BranchPriceOverride>> GetAllAsync(Guid branchId, Guid? courtTypeId = null);
        Task<List<BranchPriceOverride>> GetCurrentAsync(Guid branchId, Guid? courtTypeId = null);
        Task<List<BranchPriceOverride>> GetCurrentForDateAsync(Guid branchId, DateOnly targetDate, Guid? courtTypeId = null);
        Task<BranchPriceOverride?> GetByIdAsync(Guid id);
        Task<bool> ExistsAsync(Guid branchId, Guid courtTypeId, Guid timeSlotId, DateOnly effectiveFrom);
        Task CreateBatchAsync(List<BranchPriceOverride> prices);
        Task DeleteAsync(Guid id);


        Task<List<BranchPriceOverride>> GetCurrentRawAsync(Guid branchId, Guid courtTypeId);

    }
}
