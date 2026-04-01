using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Court;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.Interfaces;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
namespace SmashCourt_BE.Services
{
    public class CourtService : ICourtService
    {
        private readonly ICourtRepository _repo;
        private readonly IBranchRepository _branchRepo;
        private readonly IUserBranchRepository _userBranchRepo;
        private readonly ICourtTypeRepository _courtTypeRepo;
        private readonly ILogger<CourtService> _logger;

        public CourtService(ICourtRepository repo, IBranchRepository branchRepo,IUserBranchRepository userBranchRepo,ICourtTypeRepository courtTypeRepo,ILogger<CourtService> logger)
        {
            _repo = repo;
            _branchRepo = branchRepo;
            _userBranchRepo = userBranchRepo;
            _courtTypeRepo = courtTypeRepo;
            _logger = logger;
        }

        // lấy tất cả sân của chi nhánh
        public async Task<List<CourtDto>> GetAllByBranchAsync(
            Guid branchId, bool isStaffOrAbove)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            // Check chi nhánh có đang hoạt động không (customer không xem được chi nhánh suspended)
            if (!isStaffOrAbove && branch.Status != BranchStatus.ACTIVE)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var courts = await _repo.GetAllByBranchAsync(branchId, isStaffOrAbove);
            return courts.Select(MapToDto).ToList();
        }

        // lấy sân theo id
        public async Task<CourtDto> GetByIdAsync(Guid id, Guid branchId, bool isStaffOrAbove)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            // Check chi nhánh có đang hoạt động không (customer không xem được chi nhánh suspended)
            if (!isStaffOrAbove && branch.Status != BranchStatus.ACTIVE)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // CUSTOMER không thấy sân SUSPENDED
            if (!isStaffOrAbove && court.Status == CourtStatus.SUSPENDED)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            return MapToDto(court);
        }

        // tạo sân mới
        public async Task<CourtDto> CreateAsync(Guid branchId,CreateCourtDto dto,Guid currentUserId,string currentUserRole)
        {
            // 1. Validate branch + quyền
            var branch = await ValidateBranchAccessAsync(
                branchId, currentUserId, currentUserRole);

            // 2. Chi nhánh phải đang ACTIVE
            if (branch.Status != BranchStatus.ACTIVE)
                throw new AppException(400,
                    "Chi nhánh không đang hoạt động, không thể thêm sân",
                    ErrorCodes.BadRequest);

            // 3. Check tên sân unique trong chi nhánh
            var exists = await _repo.ExistsByNameAsync(dto.Name, branchId);
            if (exists)
                throw new AppException(409,
                    "Tên sân đã tồn tại trong chi nhánh này", ErrorCodes.Conflict);

            // 4. Check loại sân đang ACTIVE và đã bật tại chi nhánh
            var courtType = await _courtTypeRepo.GetByIdAsync(dto.CourtTypeId);
            if (courtType == null || courtType.Status != CourtTypeStatus.ACTIVE)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            var isEnabled = await _branchRepo.IsCourtTypeEnabledAsync(
                branchId, dto.CourtTypeId);
            if (!isEnabled)
                throw new AppException(400,
                    "Loại sân chưa được bật tại chi nhánh này", ErrorCodes.BadRequest);

            // 5. Tạo sân
            var court = new Court
            {
                BranchId = branchId,
                CourtTypeId = dto.CourtTypeId,
                Name = dto.Name.Trim(),
                Description = dto.Description?.Trim(),
                AvatarUrl = dto.AvatarUrl?.Trim(),
                Status = CourtStatus.AVAILABLE,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                var created = await _repo.CreateAsync(court);
                created.CourtType = courtType;
                return MapToDto(created);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("uq_courts_name_per_branch") == true)
            {
                throw new AppException(409,
                    "Tên sân đã tồn tại trong chi nhánh này", ErrorCodes.Conflict);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo sân");
                throw new AppException(500, "Đã xảy ra lỗi khi tạo sân", ErrorCodes.InternalError);
            }
        }

        // cập nhật sân
        public async Task<CourtDto> UpdateAsync(Guid id, Guid branchId, UpdateCourtDto dto,Guid currentUserId,string currentUserRole)
        {
            // 1. Validate branch + quyền
            await ValidateBranchAccessAsync(branchId, currentUserId, currentUserRole);

            // 2. Tìm sân
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // 3. Check tên unique — bỏ qua chính nó
            var exists = await _repo.ExistsByNameAsync(dto.Name, branchId, id);
            if (exists)
                throw new AppException(409,
                    "Tên sân đã tồn tại trong chi nhánh này", ErrorCodes.Conflict);

            // 4. Check loại sân mới có hợp lệ không
            var courtType = await _courtTypeRepo.GetByIdAsync(dto.CourtTypeId);
            if (courtType == null || courtType.Status != CourtTypeStatus.ACTIVE)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            var isEnabled = await _branchRepo.IsCourtTypeEnabledAsync(
                branchId, dto.CourtTypeId);
            if (!isEnabled)
                throw new AppException(400,
                    "Loại sân chưa được bật tại chi nhánh này", ErrorCodes.BadRequest);

            // 5. Update
            court.Name = dto.Name.Trim();
            court.Description = dto.Description?.Trim();
            court.AvatarUrl = dto.AvatarUrl?.Trim();
            court.CourtTypeId = dto.CourtTypeId;
            court.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _repo.UpdateAsync(court);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("uq_courts_name_per_branch") == true)
            {
                throw new AppException(409,
                    "Tên sân đã tồn tại trong chi nhánh này", ErrorCodes.Conflict);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật sân");
                throw new AppException(500, "Đã xảy ra lỗi khi cập nhật sân", ErrorCodes.InternalError);
            }

            court.CourtType = courtType;
            return MapToDto(court);
        }

        // tạm ngưng sân
        public async Task SuspendAsync(Guid id, Guid branchId, Guid currentUserId,string currentUserRole)
        {
            // 1. Validate branch + quyền
            await ValidateBranchAccessAsync(branchId, currentUserId, currentUserRole);

            // 2. Tìm sân
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // 3. Check status hiện tại
            if (court.Status == CourtStatus.SUSPENDED)
                throw new AppException(400,
                    "Sân đã bị tạm ngưng trước đó", ErrorCodes.BadRequest);

            // 4. Sân đang IN_USE → không thể suspend
            if (court.Status == CourtStatus.IN_USE)
                throw new AppException(400,
                    "Sân đang có khách chơi, không thể tạm ngưng",
                    ErrorCodes.BadRequest);

            // 5. Check booking active trong tương lai
            var hasActiveBookings = await _repo.HasActiveBookingsAsync(id);
            if (hasActiveBookings)
                throw new AppException(400,
                    "Sân đang có đơn đặt chưa hoàn thành, không thể tạm ngưng",
                    ErrorCodes.ResourceInUse);

            // 6. Chuyển status
            court.Status = CourtStatus.SUSPENDED;
            court.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(court);

            // TODO: Broadcast SignalR → CourtStatusChanged
        }

        // mở lại sân từ trạng thái SUSPENDED → AVAILABLE
        public async Task ActivateAsync( Guid id,Guid branchId,Guid currentUserId,string currentUserRole)
        {
            // 1. Validate branch + quyền
            await ValidateBranchAccessAsync(branchId, currentUserId, currentUserRole);

            // 2. Tìm sân
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // 3. Chỉ mở lại được khi đang SUSPENDED
            if (court.Status != CourtStatus.SUSPENDED)
                throw new AppException(400,
                    "Chỉ có thể mở lại sân đang bị tạm ngưng",
                    ErrorCodes.BadRequest);

            // 4. Chuyển về AVAILABLE
            court.Status = CourtStatus.AVAILABLE;
            court.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(court);

            // TODO: Broadcast SignalR → CourtStatusChanged
        }


        // xóa sân (chuyển sang INACTIVE)
        public async Task DeleteAsync( Guid id,Guid branchId,Guid currentUserId,string currentUserRole)
        {
            // 1. Validate branch + quyền
            await ValidateBranchAccessAsync(branchId, currentUserId, currentUserRole);

            // 2. Tìm sân
            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // 3. Sân đang IN_USE → không thể xóa
            if (court.Status == CourtStatus.IN_USE)
                throw new AppException(400,
                    "Sân đang có khách chơi, không thể xóa",
                    ErrorCodes.BadRequest);

            // 4. Check booking active trong tương lai
            var hasActiveBookings = await _repo.HasActiveBookingsAsync(id);
            if (hasActiveBookings)
                throw new AppException(400,
                    "Sân đang có đơn đặt chưa hoàn thành, không thể xóa",
                    ErrorCodes.ResourceInUse);

            // 5. Xóa mềm → INACTIVE
            court.Status = CourtStatus.INACTIVE;
            court.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(court);

            // TODO: Broadcast SignalR → CourtStatusChanged
        }



        // kiểm tra quyền truy cập chi nhánh và trả về chi nhánh nếu hợp lệ
        private async Task<Branch> ValidateBranchAccessAsync(
            Guid branchId, Guid currentUserId, string currentUserRole)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
            {
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(
                    currentUserId, branchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Bạn không có quyền thao tác chi nhánh này",
                        ErrorCodes.Forbidden);
            }

            return branch;
        }

        // map data sang dto 
        private static CourtDto MapToDto(Court c) => new()
        {
            Id = c.Id,
            BranchId = c.BranchId,
            CourtTypeId = c.CourtTypeId,
            CourtTypeName = c.CourtType?.Name ?? "N/A",
            Name = c.Name,
            Description = c.Description,
            AvatarUrl = c.AvatarUrl,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };
    }
}
