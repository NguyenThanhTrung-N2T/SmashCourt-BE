using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.DTOs.Branch;
namespace SmashCourt_BE.Repositories
{
    public class BranchRepository : IBranchRepository
    {
        private readonly SmashCourtContext _context;

        public BranchRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // lấy danh sách chi nhánh , có phân trang , lấy theo role 
        public async Task<PagedResult<Branch>> GetAllAsync(
            int page, int pageSize, bool includeSuspended)
        {
            var query = _context.Branches
                .Include(b => b.UserBranches.Where(ub => ub.Role == UserBranchRole.MANAGER && ub.IsActive))
                    .ThenInclude(ub => ub.User)
                .Where(b => b.Status != BranchStatus.INACTIVE);

            // CUSTOMER / chưa đăng nhập → chỉ thấy ACTIVE
            if (!includeSuspended)
                query = query.Where(b => b.Status == BranchStatus.ACTIVE);

            query = query.OrderBy(b => b.Name);

            var totalItems = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Branch>
            {
                Items = items,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };
        }


        // lấy chi nhánh theo id , chỉ lấy ACTIVE và SUSPENDED
        public async Task<Branch?> GetByIdAsync(Guid id)
        {
            return await _context.Branches
                .FirstOrDefaultAsync(b =>
                    b.Id == id &&
                    b.Status != BranchStatus.INACTIVE);
        }


        // kiểm tra tên chi nhánh đã tồn tại chưa , chỉ kiểm tra ACTIVE và SUSPENDED
        public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null)
        {
            return await _context.Branches
                .Where(b =>
                    b.Status != BranchStatus.INACTIVE &&
                    b.Name.ToLower() == name.ToLower() &&
                    (excludeId == null || b.Id != excludeId))
                .AnyAsync();
        }

        // tạo chi nhánh mới
        public async Task<Branch> CreateAsync(Branch branch)
        {
            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();
            return branch;
        }

        // tạo chi nhánh mới và gán manager cho chi nhánh đó
        public async Task<Branch> CreateWithManagerAsync(Branch branch, UserBranch userBranch)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                _context.Branches.Add(branch);
                await _context.SaveChangesAsync();

                // Gán branchId sau khi branch được tạo
                userBranch.BranchId = branch.Id;
                _context.UserBranches.Add(userBranch);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return branch;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // cập nhật chi nhánh , chỉ cập nhật ACTIVE và SUSPENDED
        public async Task UpdateAsync(Branch branch)
        {
            _context.Branches.Update(branch);
            await _context.SaveChangesAsync();
        }

        // kiểm tra chi nhánh có đang có booking nào không , chỉ kiểm tra ACTIVE và SUSPENDED
        public async Task<bool> HasActiveBookingsAsync(Guid branchId)
        {
            var activeStatuses = new[]
            {
                BookingStatus.PENDING,
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE,
                BookingStatus.IN_PROGRESS
            };

            return await _context.Bookings
                .AnyAsync(b =>
                    b.BranchId == branchId &&
                    activeStatuses.Contains(b.Status));
        }

        // lấy chi nhánh cùng với thông tin assignment của manager (nếu có) , chỉ lấy ACTIVE và SUSPENDED
        public async Task<(Branch Branch, UserBranch? ManagerAssignment)?> GetWithManagerAsync(Guid id)
        {
            var branch = await _context.Branches
                .Where(b => b.Id == id && b.Status != BranchStatus.INACTIVE)
                .FirstOrDefaultAsync();

            if (branch == null) return null;

            var managerAssignment = await _context.UserBranches
                .Include(ub => ub.User)
                .FirstOrDefaultAsync(ub =>
                    ub.BranchId == id &&
                    ub.Role == UserBranchRole.MANAGER &&
                    ub.IsActive);

            return (branch, managerAssignment);
        }

        // cập nhật thông tin loại sân của chi nhánh
        public async Task UpdateBranchCourtTypeAsync(BranchCourtType branchCourtType)
        {
            _context.BranchCourtTypes.Update(branchCourtType);
            await _context.SaveChangesAsync();
        }

        // lấy danh sách loại sân của chi nhánh , chỉ lấy ACTIVE
        public async Task<List<BranchCourtType>> GetCourtTypesAsync(Guid branchId)
        {
            return await _context.BranchCourtTypes
                .Include(bct => bct.CourtType)
                .Where(bct =>
                    bct.BranchId == branchId &&
                    bct.IsActive &&
                    bct.CourtType.Status == CourtTypeStatus.ACTIVE)
                .OrderBy(bct => bct.CourtType.Name)
                .ToListAsync();
        }

        public async Task<List<BranchCourtTypeDto>> GetAllCourtTypeDetailsAsync(Guid branchId)
        {
            var systemCourtTypes = await _context.CourtTypes
                .Where(ct => ct.Status == CourtTypeStatus.ACTIVE)
                .OrderBy(ct => ct.Name)
                .ToListAsync();

            var branchCourtTypes = await _context.BranchCourtTypes
                .Where(bct => bct.BranchId == branchId)
                .ToDictionaryAsync(bct => bct.CourtTypeId);

            var courtCounts = await _context.Courts
                .Where(c => c.BranchId == branchId && c.Status != CourtStatus.INACTIVE)
                .GroupBy(c => c.CourtTypeId)
                .Select(g => new { CourtTypeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CourtTypeId, x => x.Count);

            return systemCourtTypes.Select(ct =>
            {
                var bct = branchCourtTypes.GetValueOrDefault(ct.Id);
                var count = courtCounts.GetValueOrDefault(ct.Id, 0);

                return new BranchCourtTypeDto
                {
                    Id = bct?.Id,
                    CourtTypeId = ct.Id,
                    CourtTypeName = ct.Name,
                    CourtTypeDescription = ct.Description,
                    IsActive = bct?.IsActive ?? false,
                    CreatedAt = bct?.CreatedAt,
                    CourtCount = count
                };
            }).ToList();
        }

        // lấy thông tin loại sân của chi nhánh theo courtTypeId
        public async Task<BranchCourtType?> GetBranchCourtTypeAsync(Guid branchId, Guid courtTypeId)
        {
            return await _context.BranchCourtTypes
                .Include(bct => bct.CourtType)
                .FirstOrDefaultAsync(bct =>
                    bct.BranchId == branchId &&
                    bct.CourtTypeId == courtTypeId);
        }

        // thêm loại sân cho chi nhánh
        public async Task<BranchCourtType> AddCourtTypeAsync(BranchCourtType branchCourtType)
        {
            _context.BranchCourtTypes.Add(branchCourtType);
            await _context.SaveChangesAsync();
            return branchCourtType;
        }

        // kiẻm tra chi nhánh có loại sân nào không , chỉ kiểm tra ACTIVE
        public async Task<bool> HasCourtsWithTypeAsync(Guid branchId, Guid courtTypeId)
        {
            return await _context.Courts
                .AnyAsync(c =>
                    c.BranchId == branchId &&
                    c.CourtTypeId == courtTypeId &&
                    c.Status != CourtStatus.INACTIVE);
        }

        // lấy danh sách dịch vụ của chi nhánh , chỉ lấy ACTIVE
        public async Task<List<BranchService>> GetServicesAsync(Guid branchId)
        {
            return await _context.BranchServices
                .Include(bs => bs.Service)
                .Where(bs =>
                    bs.BranchId == branchId &&
                    bs.Status == BranchServiceStatus.ENABLED &&
                    bs.Service.Status == ServiceStatus.ACTIVE)
                .OrderBy(bs => bs.Service.Name)
                .ToListAsync();
        }

        // lấy thông tin dịch vụ của chi nhánh theo serviceId
        public async Task<BranchService?> GetBranchServiceAsync(Guid branchId, Guid serviceId)
        {
            return await _context.BranchServices
                .Include(bs => bs.Service)
                .FirstOrDefaultAsync(bs =>
                    bs.BranchId == branchId &&
                    bs.ServiceId == serviceId);
        }

        // thêm dịch vụ cho chi nhánh
        public async Task<BranchService> AddServiceAsync(BranchService branchService)
        {
            _context.BranchServices.Add(branchService);
            await _context.SaveChangesAsync();
            return branchService;
        }


        // cập nhật thông tin dịch vụ của chi nhánh
        public async Task UpdateBranchServiceAsync(Guid id, decimal price, BranchServiceStatus status)
        {
            await _context.BranchServices
                .Where(bs => bs.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(bs => bs.Price, price)
                    .SetProperty(bs => bs.Status, status)
                    .SetProperty(bs => bs.UpdatedAt, DateTime.UtcNow));
        }

        // kiểm tra loại sân có được dùng tại chi nhánh hay không
        public async Task<bool> IsCourtTypeEnabledAsync(Guid branchId, Guid courtTypeId)
        {
            return await _context.BranchCourtTypes
                .AnyAsync(bct =>
                    bct.BranchId == branchId &&
                    bct.CourtTypeId == courtTypeId &&
                    bct.IsActive);
        }
    }
}
