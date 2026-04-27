using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface ISystemPriceService
    {
        // Lịch sử toàn bộ giá chung — filter theo court type nếu có
        Task<List<CurrentPriceDto>> GetAllAsync(Guid? courtTypeId = null);

        // Giá chung đang áp dụng hiện tại
        Task<List<CurrentPriceDto>> GetCurrentAsync(Guid? courtTypeId = null);

        // Lấy giá chung resolved cho 1 ngày cụ thể
        Task<List<CurrentPriceDto>> GetResolvedAsync(DateOnly date, Guid? courtTypeId = null);

        // Tạo mới một batch giá chung (áp dụng từ ngày nào, cho court type nào, giá như thế nào)
        Task CreateBatchAsync(CreateSystemPriceDto dto);

        // Lấy danh sách các ngày hiệu lực (phiên bản giá) của một loại sân cụ thể
        Task<List<PriceVersionListDto>> GetVersionsAsync(Guid courtTypeId);

        // Lấy chi tiết một phiên bản giá chung cho ngày hiệu lực cụ thể
        Task<PriceVersionDetailDto?> GetVersionDetailAsync(Guid courtTypeId, DateOnly effectiveFrom);
    }
}
