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

        public async Task<PagedResult<Service>> GetAllAsync(int page = 1, int pageSize = 10)
        {
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(100, pageSize));

            var query = _context.Services
                .Where(s => s.Status == ServiceStatus.ACTIVE)
                .AsQueryable();

            var totalItems = await query.CountAsync();
            var items = await query
                .OrderBy(s => s.Name)
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

        public async Task<Service?> GetByIdAsync(Guid id)
        {
            return await _context.Services.FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<Service?> GetByNameAsync(string name)
        {
            return await _context.Services
                .FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower() && s.Status == ServiceStatus.ACTIVE);
        }

        public async Task<Service> CreateAsync(Service service)
        {
            _context.Services.Add(service);
            await _context.SaveChangesAsync();
            return service;
        }

        public async Task<Service?> UpdateAsync(Service service)
        {
            _context.Services.Update(service);
            await _context.SaveChangesAsync();
            return service;
        }

        public async Task<bool> SoftDeleteAsync(Guid id)
        {
            var service = await GetByIdAsync(id);
            if (service == null) return false;

            service.Status = ServiceStatus.DELETED;
            service.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetActiveBranchCountAsync(Guid id)
        {
            return await _context.BranchServices
                .CountAsync(bs => bs.ServiceId == id && bs.Status == BranchServiceStatus.ENABLED);
        }
    }
}

