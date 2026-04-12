using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ICourtRepository
    {
        // lấy tất cả sân của chi nhánh
        Task<List<Court>> GetAllByBranchAsync(Guid branchId, bool isStaffOrAbove);

        // lấy sân theo id, branchId là tùy chọn — khi truyền vào sẽ scope theo chi nhánh (dùng cho staff), khi null sẽ lấy theo id đơn thuần (dùng khi booking)
        Task<Court?> GetByIdAsync(Guid id, Guid? branchId = null);

        // kiểm tra tên sân đã tồn tại trong chi nhánh chưa, nếu excludeId được cung cấp thì sẽ bỏ qua sân có id đó (dùng cho update)
        Task<bool> ExistsByNameAsync(string name, Guid branchId, Guid? excludeId = null);

        // kiểm tra sân có đang bị đặt hoặc đang sử dụng hay không, nếu có thì không được phép xóa
        Task<bool> HasActiveBookingsAsync(Guid courtId);

        // tạo sân mới
        Task<Court> CreateAsync(Court court);

        // cập nhật thông tin sân
        Task UpdateAsync(Court court);
    }
}
