using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface ISystemPriceService
    {
        // Lịch sử toàn bộ giá chung — filter theo court type nếu có
        Task<List<CurrentPriceDto>> GetAllAsync(Guid? courtTypeId = null);

        // Giá chung đang áp dụng hiện tại
        Task<List<CurrentPriceDto>> GetCurrentAsync(Guid? courtTypeId = null);

        // Tạo mới một batch giá chung (áp dụng từ ngày nào, cho court type nào, giá như thế nào)
        Task CreateBatchAsync(CreateSystemPriceDto dto);
    }
}
