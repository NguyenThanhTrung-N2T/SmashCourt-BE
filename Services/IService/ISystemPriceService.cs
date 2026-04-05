using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface ISystemPriceService
    {
        Task<List<CurrentPriceDto>> GetAllAsync(Guid? courtTypeId = null);
        Task<List<CurrentPriceDto>> GetCurrentAsync(Guid? courtTypeId = null);
        Task CreateBatchAsync(CreateSystemPriceDto dto);
    }
}
