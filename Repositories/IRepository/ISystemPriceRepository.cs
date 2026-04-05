using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ISystemPriceRepository
    {
        Task<List<SystemPrice>> GetAllAsync(Guid? courtTypeId = null);
        Task<List<SystemPrice>> GetCurrentAsync(Guid? courtTypeId = null);
        Task<bool> ExistsAsync(Guid courtTypeId, Guid timeSlotId, DateOnly effectiveFrom);
        Task CreateBatchAsync(List<SystemPrice> prices);

        Task<List<SystemPrice>> GetCurrentRawAsync(Guid courtTypeId);
    }
}
