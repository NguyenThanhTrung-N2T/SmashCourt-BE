using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchPriceService
    {
        // Trả về giá thực tế — branch override nếu có, fallback về system price.
        Task<List<EffectivePriceDto>> GetEffectiveCurrentAsync(Guid? requestedBranchId, Guid? courtTypeId, Guid currentUserId, string currentUserRole);

        // Lấy snapshot giá thực tế cho 1 ngày cụ thể (branch override nếu có, fallback về system price)
        Task<List<EffectivePriceDto>> GetEffectiveResolvedAsync(Guid? requestedBranchId, DateOnly date, Guid? courtTypeId, Guid currentUserId, string currentUserRole);

        // List branch override price versions by effective date.
        Task<List<PriceVersionListDto>> GetVersionsAsync(Guid? requestedBranchId, Guid courtTypeId, Guid currentUserId, string currentUserRole);

        // Lấy chi tiết một phiên bản giá chi nhánh (override) cho ngày hiệu lực cụ thể
        Task<BranchPriceVersionDetailDto?> GetVersionDetailAsync(Guid? requestedBranchId, Guid courtTypeId, DateOnly effectiveFrom, Guid currentUserId, string currentUserRole);

        // Tạo batch giá override mới cho 1 branch + court type với ngày hiệu lực cụ thể.
        Task CreateBatchAsync(Guid? requestedBranchId, CreateBranchPriceDto dto, Guid currentUserId, string currentUserRole);

        // Xóa batch giá override của 1 branch + court type + khung giờ với ngày hiệu lực cụ thể.
        Task DeleteAsync(Guid? requestedBranchId, DeleteBranchPriceDto dto, Guid currentUserId, string currentUserRole);

        // Tính giá cho 1 booking dựa trên branch override nếu có, fallback về system price.
        Task<CalculatePriceResultDto> CalculateAsync(Guid branchId, CalculatePriceDto dto);
    }
}
