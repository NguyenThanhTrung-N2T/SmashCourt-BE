using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchPriceService
    {
        Task<List<CurrentPriceDto>> GetAllAsync(Guid branchId, Guid? courtTypeId = null);
        Task<List<CurrentPriceDto>> GetCurrentAsync(Guid branchId, Guid? courtTypeId = null);
        Task CreateBatchAsync(Guid branchId, CreateBranchPriceDto dto);
        Task DeleteAsync(Guid branchId, Guid id);
        Task<CalculatePriceResultDto> CalculateAsync(Guid branchId, CalculatePriceDto dto);
    }
}
