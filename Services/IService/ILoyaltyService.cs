using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Loyalty;

namespace SmashCourt_BE.Services.IService
{
    public interface ILoyaltyService
    {
        // lấy thông tin loyalty của user
        Task<MyLoyaltyDto> GetMyLoyaltyAsync(Guid userId);

        // lấy lịch sử giao dịch của user
        Task<PagedResult<LoyaltyTransactionDto>> GetMyTransactionsAsync(
    Guid userId, PaginationQuery query);
    }
}
