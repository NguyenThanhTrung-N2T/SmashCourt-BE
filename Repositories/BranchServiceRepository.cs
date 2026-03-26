using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories
{
    public class BranchServiceRepository : IBranchServiceRepository
    {
        private readonly SmashCourtContext _context;

        public BranchServiceRepository(SmashCourtContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<BranchService>> GetByBranchAsync(Guid branchId, int page = 1, int pageSize = 10)
        {
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(100, pageSize));

            var query = _context.BranchServices
                .Where(bs => bs.BranchId == branchId)
                .Include(bs => bs.Service)
                .AsQueryable();

            var totalItems = await query.CountAsync();
            var items = await query
                .OrderBy(bs => bs.Service.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<BranchService>
            {
                Items = items,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<BranchService?> GetByBranchServiceAsync(Guid branchId, Guid serviceId)
        {
            return await _context.BranchServices
                .FirstOrDefaultAsync(bs => bs.BranchId == branchId && bs.ServiceId == serviceId);
        }

        public async Task<BranchService> CreateAsync(BranchService branchService)
        {
            _context.BranchServices.Add(branchService);
            await _context.SaveChangesAsync();
            return branchService;
        }

        public async Task<BranchService?> UpdateAsync(BranchService branchService)
        {
            _context.BranchServices.Update(branchService);
            await _context.SaveChangesAsync();
            return branchService;
        }

        public async Task<bool> SoftDeleteAsync(Guid branchId, Guid serviceId)
        {
            var branchService = await GetByBranchServiceAsync(branchId, serviceId);
            if (branchService == null) return false;

            branchService.Status = BranchServiceStatus.DISABLED;
            branchService.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }
    }
}

