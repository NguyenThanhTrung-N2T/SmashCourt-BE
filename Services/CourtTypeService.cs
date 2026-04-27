using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

public class CourtTypeService : ICourtTypeService
{
    private readonly ICourtTypeRepository _repository;

    public CourtTypeService(ICourtTypeRepository repository)
    {
        _repository = repository;
    }

    // Lấy danh sách loại sân đang ACTIVE, có phân trang
    public async Task<PagedResult<CourtTypeDto>> GetAllCourtTypesAsync(PaginationQuery query)
    {
        var pagedResult = await _repository.GetAllAsync(query.Page, query.PageSize);

        return new PagedResult<CourtTypeDto>
        {
            Items = pagedResult.Items.Select(MapToDto),
            TotalItems = pagedResult.TotalItems,
            Page = pagedResult.Page,
            PageSize = pagedResult.PageSize
        };
    }

    // Lấy chi tiết loại sân theo ID — kèm count thực tế từ DB
    public async Task<CourtTypeDto> GetByIdAsync(Guid id)
    {
        var result = await _repository.GetWithCountByIdAsync(id);
        if (result == null)
            throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

        return MapToDto(result);
    }

    // Tạo mới loại sân
    public async Task<CourtTypeDto> CreateAsync(CreateCourtTypeDto dto)
    {
        // Check tên unique
        var exists = await _repository.ExistsByNameAsync(dto.Name);
        if (exists)
            throw new AppException(409, "Tên loại sân đã tồn tại", ErrorCodes.NameDuplicate);

        var courtType = new CourtType
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Status = CourtTypeStatus.ACTIVE,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            var created = await _repository.CreateAsync(courtType);
            return MapToDto(new CourtTypeWithCount
            {
                CourtType = created,
                ActiveBranchCount = 0,
                CourtCount = 0
            });
        }
        catch (DbUpdateException)
        {
            throw new AppException(409, "Tên loại sân đã tồn tại", ErrorCodes.NameDuplicate);
        }
    }

    // Cập nhật loại sân
    public async Task<CourtTypeDto> UpdateAsync(Guid id, UpdateCourtTypeDto dto)
    {
        var courtType = await _repository.GetByIdAsync(id);
        if (courtType == null)
            throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

        // Check tên unique — bỏ qua chính nó (repo đã xử lý case-insensitive)
        var exists = await _repository.ExistsByNameAsync(dto.Name, id);
        if (exists)
            throw new AppException(409, "Tên loại sân đã tồn tại", ErrorCodes.NameDuplicate);

        courtType.Name = dto.Name.Trim();
        courtType.Description = dto.Description?.Trim();
        courtType.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _repository.UpdateAsync(courtType);
        }
        catch (DbUpdateException)
        {
            throw new AppException(409, "Tên loại sân đã tồn tại", ErrorCodes.NameDuplicate);
        }

        // Lấy lại với count thực tế sau khi update
        var updated = await _repository.GetWithCountByIdAsync(id)
            ?? throw new AppException(404, "Không tìm thấy loại sân sau khi cập nhật", ErrorCodes.NotFound);
        return MapToDto(updated);   
    }

    // Xóa loại sân (soft delete)
    public async Task DeleteAsync(Guid id)
    {
        var courtType = await _repository.GetByIdAsync(id);
        if (courtType == null)
            throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

        // Kiểm tra có đang được dùng ở chi nhánh nào không
        var isInUse = await _repository.IsInUseAsync(id);
        if (isInUse)
            throw new AppException(400,
                "Loại sân đang được sử dụng tại một số chi nhánh, không thể xóa",
                ErrorCodes.ResourceInUse);

        // Xóa mềm — đổi status sang DELETED
        courtType.Status = CourtTypeStatus.DELETED;
        courtType.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(courtType);
    }

    // Mapper
    private static CourtTypeDto MapToDto(CourtTypeWithCount x) => new()
    {
        Id = x.CourtType.Id,
        Name = x.CourtType.Name,
        Description = x.CourtType.Description,
        Status = x.CourtType.Status,
        CreatedAt = x.CourtType.CreatedAt,
        UpdatedAt = x.CourtType.UpdatedAt,
        ActiveBranchCount = x.ActiveBranchCount,
        CourtCount = x.CourtCount
    };
}
