using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Services.IService
{
    public interface ISystemPriceService
    {
        // GET /api/system-prices/versions
        // Returns all version dates for a court type with their statuses (ACTIVE, SCHEDULED, EXPIRED)
        Task<SystemPriceVersionsResponse> GetVersionsAsync(Guid courtTypeId);

        // GET /api/system-prices/versions/{effectiveFrom}
        // Returns the exact set of slots configured on a specific version date
        Task<SystemPriceVersionDetailDto> GetVersionDetailAsync(Guid courtTypeId, DateOnly effectiveFrom);

        // PATCH /api/system-prices/versions/{effectiveFrom}
        // Creates or partially updates a system price version
        // Returns (Response, IsCreated) tuple for proper HTTP status code handling
        Task<(SystemPriceVersionDetailDto Response, bool IsCreated)> UpsertVersionAsync(
            Guid courtTypeId,
            DateOnly effectiveFrom,
            UpsertPriceRequest request);

        // DELETE /api/system-prices/versions/{effectiveFrom}
        // Deletes an entire system price version
        // Only SCHEDULED (future) versions can be deleted
        Task DeleteVersionAsync(Guid courtTypeId, DateOnly effectiveFrom);

        // Lấy giá chung resolved cho 1 ngày cụ thể (used for internal calculations)
        Task<List<CurrentPriceDto>> GetEffectivePricesAsync(DateOnly date, Guid? courtTypeId = null);
    }
}
