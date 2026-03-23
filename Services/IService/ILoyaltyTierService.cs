using SmashCourt_BE.DTOs.LoyaltyTier;

namespace SmashCourt_BE.Services.IService
{
    public interface ILoyaltyTierService
    {
        // Lấy danh sách tất cả các rank khách hàng, sắp xếp theo MinPoints tăng dần
        Task<IEnumerable<LoyaltyTierDto>> GetAllLoyaltyTiersAsync();

        // Lấy thông tin chi tiết của một rank khách hàng theo Id
        Task<LoyaltyTierDto> GetLoyaltyTierByIdAsync(Guid id);

        // Cập nhật thông tin của một rank khách hàng
        Task<LoyaltyTierDto> UpdateLoyaltyTierAsync(Guid id, UpdateLoyaltyTierDto dto);

    }
}
