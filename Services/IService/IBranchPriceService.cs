using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface IBranchPriceService
    {
        // Xóa batch giá override của 1 branch + court type + khung giờ với ngày hiệu lực cụ thể.
        Task DeleteVersionAsync(Guid? requestedBranchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            Guid currentUserId,
            string currentUserRole);

        // Tính giá cho 1 booking dựa trên branch override nếu có, fallback về system price.
        Task<CalculatePriceResultDto> CalculateAsync(Guid? branchId, CalculatePriceDto dto);

        // [NEW] GET /api/prices - Lấy snapshot giá effective cho chi nhánh tại một ngày cụ thể
        // Trả về EffectivePricesResponse đã được group theo court type và merge consecutive slots
        Task<EffectivePricesResponse> GetEffectivePricesAsync(
            Guid? requestedBranchId,
            DateOnly date,
            Guid? courtTypeId,
            Guid currentUserId,
            string currentUserRole);

        // [NEW] GET /api/prices/overrides - Lấy danh sách các phiên bản giá override cho một loại sân
        // Trả về danh sách effective_from với status (ACTIVE, SCHEDULED, EXPIRED)
        Task<PriceOverrideVersionsResponse> GetPriceOverrideVersionsAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            Guid currentUserId,
            string currentUserRole);

        // [NEW] GET /api/prices/overrides/{effectiveFrom} - Lấy chi tiết cấu hình giá override cho một ngày hiệu lực cụ thể
        // Trả về exact override version configuration (không phải resolved snapshot)
        Task<PriceOverrideVersionDetailDto> GetPriceOverrideVersionDetailAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            Guid currentUserId,
            string currentUserRole);

        // [NEW] PUT /api/prices/overrides/{effectiveFrom} - Tạo hoặc cập nhật phiên bản giá override
        // Trả về (PriceOverrideVersionDetailDto, isCreated) - isCreated = true nếu tạo mới, false nếu update
        Task<(PriceOverrideVersionDetailDto Response, bool IsCreated)> UpsertPriceOverrideVersionAsync(
            Guid? requestedBranchId,
            Guid courtTypeId,
            DateOnly effectiveFrom,
            UpsertPriceRequest request,
            Guid currentUserId,
            string currentUserRole);
    }
}
