using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ICancelPolicyRepository
    {
        // Lấy tất cả chính sách hủy, sắp xếp theo HoursBefore tăng dần
        Task<IEnumerable<CancelPolicy>> GetAllAsync();

        // Lấy chính sách hủy theo Id
        Task<CancelPolicy?> GetByIdAsync(Guid id);

        // Lấy chính sách hủy theo số giờ trước khi đặt sân, trả về null nếu không tìm thấy
        Task<CancelPolicy?> GetByHoursBeforeAsync(int hoursBefore);

        // Tạo mới một chính sách hủy
        Task<CancelPolicy> CreateAsync(CancelPolicy policy);

        // Cập nhật một chính sách hủy đã tồn tại
        Task UpdateAsync(CancelPolicy policy);

        // Xóa một chính sách hủy
        Task DeleteAsync(CancelPolicy policy);

        // Đếm số lượng chính sách hủy hiện có
        Task<int> CountAsync();
    }
}
