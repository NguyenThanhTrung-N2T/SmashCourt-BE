using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ICustomerHistoryRepository
    {
        // Lấy dữ liệu thô các booking đã kết thúc hoặc đã hủy của khách (trừ booking đang xét) để tính lịch sử gửi TADSS
        Task<IReadOnlyList<CustomerHistoryEntry>> GetEntriesAsync(
            Guid customerId, Guid excludeBookingId, CancellationToken cancellationToken = default);
    }
}
