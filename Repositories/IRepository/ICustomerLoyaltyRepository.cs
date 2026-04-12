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
    }
}
