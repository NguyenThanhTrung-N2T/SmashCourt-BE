using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using System.Threading.Tasks;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IServiceRepository
    {
        // lấy tất cả dịch vụ đang hoạt động, có phân trang
        Task<PagedResult<Service>> GetAllAsync(int page, int pageSize);

        // lấy dịch vụ theo id
        Task<Service?> GetByIdAsync(Guid id);

        // kiểm tra xem tên dịch vụ đã tồn tại chưa, nếu excludeId được cung cấp thì sẽ bỏ qua dịch vụ có id đó (dùng cho update)
        Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null);

        // tạo mới dịch vụ
        Task<Service> CreateAsync(Service service);

        // cập nhật dịch vụ
        Task UpdateAsync(Service service);
    }
}

