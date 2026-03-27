using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.Interfaces;

namespace SmashCourt_BE.Repositories;

public class CourtTypeRepository : ICourtTypeRepository
{
    private readonly SmashCourtContext _context;

    public CourtTypeRepository(SmashCourtContext context)
    {
        _context = context;
    }

    // Lấy tất cả loại sân đang active, kèm số lượng chi nhánh đang dùng loại sân đó và số lượng sân (không tính INACTIVE)
    public async Task<PagedResult<CourtTypeWithCount>> GetAllAsync(int page, int pageSize)
    {
        var query = _context.CourtTypes
            .Where(ct => ct.Status == CourtTypeStatus.ACTIVE)
            .OrderBy(ct => ct.Name)
            .Select(ct => new CourtTypeWithCount
            {
                CourtType = ct,
                // Đếm chi nhánh đang active dùng loại sân này
                ActiveBranchCount = ct.BranchCourtTypes
                    .Count(bct => bct.IsActive),
                // Đếm sân không bị INACTIVE
                CourtCount = ct.Courts
                    .Count(c => c.Status != CourtStatus.INACTIVE)
            });

        var totalItems = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<CourtTypeWithCount>
        {
            Items = items,
            TotalItems = totalItems,
            Page = page,
            PageSize = pageSize
        };
    }

    // Lấy loại sân theo id
    public async Task<CourtType?> GetByIdAsync(Guid id)
    {
        return await _context.CourtTypes.FindAsync(id);
    }

    // Check tên unique trong ACTIVE — excludeId dùng khi update
    public async Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null)
    {
        return await _context.CourtTypes
            .Where(ct =>
                ct.Status == CourtTypeStatus.ACTIVE &&
                ct.Name.ToLower() == name.ToLower() &&
                (excludeId == null || ct.Id != excludeId))
            .AnyAsync();
    }


    // Tạo mới loại sân
    public async Task<CourtType> CreateAsync(CourtType courtType)
    {
        _context.CourtTypes.Add(courtType);
        await _context.SaveChangesAsync();
        return courtType;
    }

    // Cập nhật loại sân
    public async Task UpdateAsync(CourtType courtType)
    {
        _context.CourtTypes.Update(courtType);
        await _context.SaveChangesAsync();
    }

    // Kiểm tra loại sân có đang được dùng ở chi nhánh nào không
    public async Task<bool> IsInUseAsync(Guid id)
    {
        return await _context.BranchCourtTypes
            .AnyAsync(bct =>
                bct.CourtTypeId == id &&
                bct.IsActive);
    }
}