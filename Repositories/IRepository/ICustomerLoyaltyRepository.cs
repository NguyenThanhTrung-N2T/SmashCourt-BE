using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ICustomerLoyaltyRepository
    {
        // lấy thông tin điểm tích lũy của khách hàng theo userId
        Task<CustomerLoyalty?> GetByUserIdAsync(Guid userId);

        // tạo thông tin điểm tích lũy của khách hàng mới
        Task CreateAsync(CustomerLoyalty customerLoyalty);

        // cập nhật thông tin điểm tích lũy của khách hàng
        Task UpdateAsync(CustomerLoyalty loyalty);

        /// <summary>
        /// Cộng/trừ điểm loyalty một cách atomic (tránh race condition)
        /// </summary>
        /// <param name="userId">ID của user</param>
        /// <param name="points">Số điểm cần cộng (dương) hoặc trừ (âm)</param>
        /// <returns>Tổng điểm mới sau khi cập nhật</returns>
        Task<int> AddPointsAtomicAsync(Guid userId, int points);

        /// <summary>
        /// Cập nhật tier của user
        /// </summary>
        /// <param name="userId">ID của user</param>
        /// <param name="tierId">ID của tier mới</param>
        Task UpdateTierAsync(Guid userId, Guid tierId);
    }
}
