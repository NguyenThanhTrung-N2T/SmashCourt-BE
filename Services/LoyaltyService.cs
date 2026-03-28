using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Loyalty;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class LoyaltyService : ILoyaltyService
    {
        private readonly ICustomerLoyaltyRepository _loyaltyRepo;
        private readonly ILoyaltyTierRepository _tierRepo;
        private readonly ILoyaltyTransactionRepository _transactionRepo;

        public LoyaltyService(
            ICustomerLoyaltyRepository loyaltyRepo,
            ILoyaltyTierRepository tierRepo,
            ILoyaltyTransactionRepository transactionRepo)
        {
            _loyaltyRepo = loyaltyRepo;
            _tierRepo = tierRepo;
            _transactionRepo = transactionRepo;
        }

        // Lấy thông tin loyalty của user hiện tại
        public async Task<MyLoyaltyDto> GetMyLoyaltyAsync(Guid userId)
        {
            // 1. Tìm loyalty của user
            var loyalty = await _loyaltyRepo.GetByUserIdAsync(userId);
            if (loyalty == null)
                throw new AppException(404, "Không tìm thấy thông tin loyalty", ErrorCodes.NotFound);

            // 2. Tìm tier tiếp theo
            var nextTier = await _tierRepo.GetNextTierAsync(loyalty.Tier.MinPoints);
            var isMaxTier = nextTier == null;

            // 3. Tính progress
            int? pointsToNextTier = null;
            decimal? progressPercent = null;

            if (!isMaxTier && nextTier != null)
            {
                pointsToNextTier = nextTier.MinPoints - loyalty.TotalPoints;

                // % = (điểm hiện tại - min tier hiện tại)
                //     / (min tier kế - min tier hiện tại) * 100
                var range = nextTier.MinPoints - loyalty.Tier.MinPoints;
                var earned = loyalty.TotalPoints - loyalty.Tier.MinPoints;
                progressPercent = range > 0
                    ? Math.Max(0, Math.Min(100,
                        Math.Round((decimal)earned / range * 100, 1)))
                    : 100;
            }

            return new MyLoyaltyDto
            {
                TierName = loyalty.Tier.Name,
                TotalPoints = loyalty.TotalPoints,
                DiscountRate = loyalty.Tier.DiscountRate,
                NextTierName = nextTier?.Name,
                PointsToNextTier = pointsToNextTier,
                ProgressPercent = progressPercent,
                IsMaxTier = isMaxTier
            };
        }

        // Lấy lịch sử giao dịch loyalty của user hiện tại
        public async Task<PagedResult<LoyaltyTransactionDto>> GetMyTransactionsAsync(
    Guid userId, PaginationQuery query)
        {
            var (items, total) = await _transactionRepo.GetByUserIdAsync(
                userId, query.Page, query.PageSize);

            return new PagedResult<LoyaltyTransactionDto>
            {
                Items = items.Select(MapToTransactionDto),
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = total
            };
        }

        // map từ entity sang dto
        private static LoyaltyTransactionDto MapToTransactionDto(
            LoyaltyTransaction t) => new()
            {
                Id = t.Id,
                BookingId = t.BookingId,
                Points = t.Points,
                TotalPointsAfter = t.TotalPointsAfter,
                Type = t.Type.ToString(),
                Note = t.Note,
                CreatedAt = t.CreatedAt
            };

    }
}
