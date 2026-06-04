using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CourtType;
using SmashCourt_BE.DTOs.Branch;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Services.Helpers;

namespace SmashCourt_BE.Services;

public class CourtTypeService : ICourtTypeService
{
    private readonly ICourtTypeRepository _repository;
    private readonly IBranchRepository _branchRepo;
    private readonly ICourtRepository _courtRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly IBranchScopeResolver _branchScopeResolver;

    public CourtTypeService(
        ICourtTypeRepository repository,
        IBranchRepository branchRepo,
        ICourtRepository courtRepo,
        IUserBranchRepository userBranchRepo,
        IBranchScopeResolver branchScopeResolver)
    {
        _repository = repository;
        _branchRepo = branchRepo;
        _courtRepo = courtRepo;
        _userBranchRepo = userBranchRepo;
        _branchScopeResolver = branchScopeResolver;
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

    // Lấy danh sách TẤT CẢ loại sân (kèm trạng thái bật/tắt và số lượng sân) tại chi nhánh
    public async Task<List<BranchCourtTypeDto>> GetCourtTypesAsync(Guid? requestedBranchId, Guid currentUserId, string currentUserRole)
    {
        if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
            throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

        var branchId = await _branchScopeResolver.ResolveOptionalBranchIdAsync(requestedBranchId, currentUserId, roleEnum);
        
        // ResolveOptional can return null for OWNER (all branches), but for court types we usually need a specific branch
        if (!branchId.HasValue)
            throw new AppException(400, "Vui lòng chọn chi nhánh", ErrorCodes.BadRequest);

        return await _branchRepo.GetAllCourtTypeDetailsAsync(branchId.Value);
    }

    // Thêm loại sân vào chi nhánh (bật loại sân)
    public async Task<BranchCourtTypeDto> AddCourtTypeAsync(Guid? requestedBranchId, AddCourtTypeToBranchDto dto, Guid currentUserId, string currentUserRole)
    {
        if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
            throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

        var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(requestedBranchId, currentUserId, roleEnum);

        // 1. Tìm branch
        var branch = await _branchRepo.GetByIdAsync(branchId);
        if (branch == null)
            throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);
        
        // Check chi nhánh đang hoạt động
        if (branch.Status != BranchStatus.ACTIVE)
            throw new AppException(400, "Chi nhánh không đang hoạt động, không thể thêm loại sân", ErrorCodes.BadRequest);

        // 3. Tìm court type
        var courtType = await _repository.GetByIdAsync(dto.CourtTypeId);
        if (courtType == null)
            throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

        if (courtType.Status != CourtTypeStatus.ACTIVE)
            throw new AppException(400, "Loại sân không còn hoạt động", ErrorCodes.BadRequest);

        // 4. Kiểm tra đã bật chưa
        var existing = await _branchRepo.GetBranchCourtTypeAsync(branchId, dto.CourtTypeId);
        if (existing != null)
        {
            // Đã tồn tại nhưng đang tắt → bật lại
            if (!existing.IsActive)
            {
                existing.IsActive = true;
                await _branchRepo.UpdateBranchCourtTypeAsync(existing);

                // Đếm số sân active của court type này trong chi nhánh
                var courts = await _courtRepo.GetAllByBranchAsync(branchId, true);
                var courtCount = courts.Count(c => c.CourtTypeId == dto.CourtTypeId && c.Status != CourtStatus.INACTIVE);

                return MapToCourtTypeDto(existing, courtCount);
            }
            // Đang bật rồi → conflict
            throw new AppException(409, "Loại sân này đã được bật tại chi nhánh", ErrorCodes.Conflict);
        }

        // 5. Tạo mới
        var branchCourtType = new BranchCourtType
        {
            BranchId = branchId,
            CourtTypeId = dto.CourtTypeId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _branchRepo.AddCourtTypeAsync(branchCourtType);

        var reloaded = await _branchRepo.GetBranchCourtTypeAsync(branchId, dto.CourtTypeId)
            ?? throw new AppException(500, "Lỗi khi tạo loại sân", ErrorCodes.InternalError);

        // Đếm số sân active của court type này trong chi nhánh
        var reloadedCourts = await _courtRepo.GetAllByBranchAsync(branchId, true);
        var reloadedCourtCount = reloadedCourts.Count(c => c.CourtTypeId == dto.CourtTypeId && c.Status != CourtStatus.INACTIVE);

        return MapToCourtTypeDto(reloaded, reloadedCourtCount);
    }

    // Xóa loại sân khỏi chi nhánh (tắt loại sân)
    public async Task RemoveCourtTypeAsync(Guid? requestedBranchId, Guid courtTypeId, Guid currentUserId, string currentUserRole)
    {
        if (!Enum.TryParse<UserRole>(currentUserRole, true, out var roleEnum))
            throw new AppException(403, "Role không hợp lệ", ErrorCodes.Forbidden);

        var branchId = await _branchScopeResolver.ResolveRequiredBranchIdAsync(requestedBranchId, currentUserId, roleEnum);

        // 1. Tìm branch
        var branch = await _branchRepo.GetByIdAsync(branchId);
        if (branch == null)
            throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

        // 2. Tìm BranchCourtType
        var branchCourtType = await _branchRepo.GetBranchCourtTypeAsync(branchId, courtTypeId);
        if (branchCourtType == null)
            throw new AppException(404, "Loại sân không tồn tại tại chi nhánh này", ErrorCodes.NotFound);

        if (!branchCourtType.IsActive)
            throw new AppException(400, "Loại sân này đã được tắt trước đó", ErrorCodes.BadRequest);

        // 3. Check có sân nào đang dùng loại sân này không
        var hasCourts = await _branchRepo.HasCourtsWithTypeAsync(branchId, courtTypeId);
        if (hasCourts)
            throw new AppException(400, "Loại sân đang được sử dụng, không thể bỏ", ErrorCodes.ResourceInUse);

        // 4. Soft delete
        branchCourtType.IsActive = false;
        await _branchRepo.UpdateBranchCourtTypeAsync(branchCourtType);
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

    private static BranchCourtTypeDto MapToCourtTypeDto(BranchCourtType bct, int courtCount) => new()
    {
        Id = bct.Id,
        CourtTypeId = bct.CourtTypeId,
        CourtTypeName = bct.CourtType?.Name ?? "N/A",
        CourtTypeDescription = bct.CourtType?.Description ?? "N/A",
        IsActive = bct.IsActive,
        CreatedAt = bct.CreatedAt,
        CourtCount = courtCount
    };
}
