using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories
{
    public class ServiceRepository : IServiceRepository
    {
        private readonly SmashCourtContext _context;

        public ServiceRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // lấy danh sách dịch vụ có phân trang, chỉ lấy những dịch vụ có trạng thái ACTIVE
        public async Task<PagedResult<Service>> GetAllAsync(int page, int pageSize)
        {
            var query = _context.Services
                .Where(s => s.Status == ServiceStatus.ACTIVE)
                .OrderBy(s => s.Name);

            var totalItems = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Service>
            {
                Items = items,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };
        }

        // lấy dịch vụ theo id, chỉ trả về nếu dịch vụ đó đang ACTIVE
        public async Task<Service?> GetByIdAsync(Guid id)
        {
            return await _context.Services
                .FirstOrDefaultAsync(s =>
                    s.Id == id &&
                    s.Status == ServiceStatus.ACTIVE);
        }

        // kiểm tra xem có dịch vụ nào đang ACTIVE với tên trùng với tham số name hay không, nếu excludeId được cung cấp thì sẽ bỏ qua dịch vụ có id đó (dùng để kiểm tra khi update)
        public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null)
        {
            return await _context.Services
                .Where(s =>
                    s.Status == ServiceStatus.ACTIVE &&
                    s.Name.ToLower() == name.ToLower() &&
                    (excludeId == null || s.Id != excludeId))
                .AnyAsync();
        }

        // tạo mới dịch vụ, mặc định trạng thái sẽ là ACTIVE
        public async Task<Service> CreateAsync(Service service)
        {
            _context.Services.Add(service);
            await _context.SaveChangesAsync();
            return service;
        }

        // cập nhật dịch vụ, chỉ cho phép cập nhật nếu dịch vụ đó đang ACTIVE
        public async Task UpdateAsync(Service service)
        {
            _context.Services.Update(service);
            await _context.SaveChangesAsync();
        }
    }
}

