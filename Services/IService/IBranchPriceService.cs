using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchPriceService
    {
        // Lấy tất cả cấu hình giá override của chi nhánh, có thể filter theo courtTypeId
        Task<List<CurrentPriceDto>> GetAllAsync(Guid branchId, Guid? courtTypeId = null);

        // Trả về giá thực tế — branch override nếu có, fallback về system price.
        Task<List<EffectivePriceDto>> GetEffectiveCurrentAsync(Guid branchId, Guid? courtTypeId = null);

        // Tạo batch giá override mới cho 1 branch + court type với ngày hiệu lực cụ thể.
        Task CreateBatchAsync(Guid branchId, CreateBranchPriceDto dto);

        // Xóa batch giá override của 1 branch + court type + khung giờ với ngày hiệu lực cụ thể.
        Task DeleteAsync(Guid branchId, DeleteBranchPriceDto dto);

        // Tính giá cho 1 booking dựa trên branch override nếu có, fallback về system price.
        Task<CalculatePriceResultDto> CalculateAsync(Guid branchId, CalculatePriceDto dto);
    }
}
