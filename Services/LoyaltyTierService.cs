using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.LoyaltyTier;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class LoyaltyTierService : ILoyaltyTierService
    {
        private readonly ILoyaltyTierRepository _tierRepo;
        private readonly ICustomerLoyaltyRepository _customerLoyaltyRepo;

        public LoyaltyTierService(ILoyaltyTierRepository tierRepo, ICustomerLoyaltyRepository customerLoyaltyRepo)
        {
            _tierRepo = tierRepo;
            _customerLoyaltyRepo = customerLoyaltyRepo;
        }

        // Lấy tất cả hạng thành viên
        public async Task<IEnumerable<LoyaltyTierDto>> GetAllLoyaltyTiersAsync()
        {
            var tiers = await _tierRepo.GetAllLoyaltyTiersAsync();
            return tiers.Select(MapToDto);
        }

        // Lấy hạng thành viên theo ID
        public async Task<LoyaltyTierDto> GetLoyaltyTierByIdAsync(Guid id)
        {
            var tier = await _tierRepo.GetLoyaltyTierByIdAsync(id);
            if (tier == null)
                throw new AppException(404, "Không tìm thấy hạng thành viên", ErrorCodes.NotFound);

            return MapToDto(tier);
        }

        // Cập nhật hạng thành viên
        public async Task<LoyaltyTierDto> UpdateLoyaltyTierAsync(Guid id, UpdateLoyaltyTierDto dto)
        {
            // 1. Tìm tier
            var tier = await _tierRepo.GetLoyaltyTierByIdAsync(id);
            if (tier == null)
                throw new AppException(404, "Không tìm thấy hạng thành viên", ErrorCodes.NotFound);

            // 2. Bronze bắt buộc min_points = 0 — check bằng Name vì Name cố định
            if (tier.Name == "Bronze" && dto.MinPoints != 0)
                throw new AppException(400, "Hạng Bronze bắt buộc có điểm tối thiểu = 0", ErrorCodes.BadRequest);

            // 3. Kiểm tra min_points không trùng với tier khác
            var otherTiers = await _tierRepo.GetAllExceptAsync(id);
            var isDuplicate = otherTiers.Any(t => t.MinPoints == dto.MinPoints);
            if (isDuplicate)
                throw new AppException(400, "Điểm tối thiểu đã được sử dụng bởi hạng khác", ErrorCodes.Conflict);

            // 4. Kiểm tra thứ tự phân hạng đúng
            // Build danh sách đầy đủ với giá trị mới của tier hiện tại
            var allTiers = otherTiers.ToList();
            allTiers.Add(new LoyaltyTier
            {
                Id = tier.Id,
                Name = tier.Name,
                MinPoints = dto.MinPoints
            });

            // Sort theo MinPoints → kiểm tra tên phải đúng thứ tự cố định
            // Nếu Gold bị set điểm thấp hơn Silver → sau khi sort
            // Gold sẽ đứng trước Silver → tên sai vị trí → throw lỗi
            var sorted = allTiers.OrderBy(t => t.MinPoints).ToList();
            var expectedOrder = new[] { "Bronze", "Silver", "Gold", "Platinum", "Diamond" };

            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Name != expectedOrder[i])
                    throw new AppException(400,
                        $"Điểm của hạng {tier.Name} không hợp lệ — " +
                        $"phải đảm bảo thứ tự tăng dần: Bronze < Silver < Gold < Platinum < Diamond",
                        ErrorCodes.BadRequest);
            }

            // 5. Update
            tier.MinPoints = dto.MinPoints;
            tier.DiscountRate = dto.DiscountRate;
            tier.UpdatedAt = DateTime.UtcNow;
            await _tierRepo.UpdateAsync(tier);

            return MapToDto(tier);
        }

        // Map entity sang DTO
        private static LoyaltyTierDto MapToDto(LoyaltyTier tier) => new()
        {
            Id = tier.Id,
            Name = tier.Name,
            MinPoints = tier.MinPoints,
            DiscountRate = tier.DiscountRate
        };
    }
}
