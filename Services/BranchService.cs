using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Branch;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.Interfaces;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services
{
    public class BranchService : IBranchService
    {
        private readonly IBranchRepository _repo;
        private readonly IUserRepository _userRepo;
        private readonly IUserBranchRepository _userBranchRepo;
        private readonly ILogger<BranchService> _logger;
        private readonly ICourtTypeRepository _courtTypeRepo;
        private readonly IServiceRepository _serviceRepo;
        private readonly ICourtRepository _courtRepo;

        public BranchService(IBranchRepository repo, IUserRepository userRepo,
        IUserBranchRepository userBranchRepo, ILogger<BranchService> logger, ICourtTypeRepository courtTypeRepo, IServiceRepository serviceRepo, ICourtRepository courtRepo)
        {
            _repo = repo;
            _userRepo = userRepo;
            _userBranchRepo = userBranchRepo;
            _logger = logger;
            _serviceRepo = serviceRepo;
            _courtTypeRepo = courtTypeRepo;
            _courtRepo = courtRepo;
        }

        // Kiểm tra quyền truy cập chi nhánh cho MANAGER
        private async Task ValidateBranchAccessAsync(Guid branchId, Guid currentUserId, string currentUserRole)
        {
            var branch = await _repo.GetByIdAsync(branchId);
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
        }

        public async Task<PagedResult<BranchDto>> GetAllAsync(
            PaginationQuery query, bool includeSuspended)
        {
            var pagedResult = await _repo.GetAllAsync(
                query.Page, query.PageSize, includeSuspended);

            return new PagedResult<BranchDto>
            {
                Items = pagedResult.Items.Select(b => MapToDto(b, b.UserBranches?.FirstOrDefault(ub => ub.Role == UserBranchRole.MANAGER && ub.IsActive))),
                TotalItems = pagedResult.TotalItems,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize
            };
        }


        // Lấy chi nhánh theo ID, có thể bao gồm cả chi nhánh bị đình chỉ hoạt động
        public async Task<BranchDto> GetByIdAsync(Guid id, bool includeSuspended)
        {
            var result = await _repo.GetWithManagerAsync(id);
            if (result == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var (branch, manager) = result.Value;

            if (!includeSuspended && branch.Status == BranchStatus.SUSPENDED)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            return MapToDto(branch, manager);
        }

        // Tạo chi nhánh mới, đồng thời gán manager cho chi nhánh đó
        public async Task<BranchDto> CreateAsync(CreateBranchDto dto)
        {
            // 1. Convert TimeSpan → TimeOnly
            var openTime = TimeOnly.FromTimeSpan(dto.OpenTime);
            var closeTime = TimeOnly.FromTimeSpan(dto.CloseTime);

            // 2. Validate open_time < close_time
            if (openTime >= closeTime) 
                throw new AppException(400, "Giờ mở cửa phải nhỏ hơn giờ đóng cửa", ErrorCodes.BadRequest);

            // 3. Check tên unique
            var exists = await _repo.ExistsByNameAsync(dto.Name);
            if (exists)
                throw new AppException(409, "Tên chi nhánh đã tồn tại", ErrorCodes.Conflict);

            // 4. Validate manager
            var manager = await _userRepo.GetUserByIdAsync(dto.ManagerId);
            if (manager == null)
                throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.NotFound);

            if (manager.Role != UserRole.BRANCH_MANAGER)
                throw new AppException(400,
                    "Người dùng không có vai trò Quản lý chi nhánh", ErrorCodes.BadRequest);

            if (manager.Status != UserStatus.ACTIVE)
                throw new AppException(400,
                    "Tài khoản quản lý không hoạt động", ErrorCodes.BadRequest);

            // Kiểm tra manager đã đang phụ trách chi nhánh khác chưa (chỉ check MANAGER role, không block STAFF)
            var existingManagerAssignment = await _userBranchRepo.GetActiveManagerAssignmentByUserIdAsync(dto.ManagerId);
            if (existingManagerAssignment != null)
                throw new AppException(400,
                    "Quản lý này đang phụ trách chi nhánh khác", ErrorCodes.BadRequest);

            // 5. Tạo branch + gán manager — atomic transaction trong repo
            var branch = new Branch
            {
                Name = dto.Name.Trim(),
                Address = dto.Address.Trim(),
                Phone = dto.Phone?.Trim(),
                AvatarUrl = dto.AvatarUrl?.Trim(),
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                OpenTime = openTime,
                CloseTime = closeTime,
                Status = BranchStatus.ACTIVE,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var userBranch = new UserBranch
            {
                UserId = dto.ManagerId,
                Role = UserBranchRole.MANAGER,
                IsActive = true,
                AssignedAt = DateTime.UtcNow,
                User = manager
                // BranchId set trong repo sau khi branch tạo xong
            };

            try
            {
                var created = await _repo.CreateWithManagerAsync(branch, userBranch);
                return MapToDto(created, userBranch);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException?.Message.Contains("idx_branches_name_active") == true)
            {
                throw new AppException(409, "Tên chi nhánh đã tồn tại", ErrorCodes.Conflict);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo chi nhánh");
                throw new AppException(500, "Đã xảy ra lỗi khi tạo chi nhánh", ErrorCodes.InternalError);
            }
        }

        // Cập nhật thông tin chi nhánh
        public async Task<BranchDto> UpdateAsync(Guid id, UpdateBranchDto dto)
        {
            // 1. Tìm branch
            var branch = await _repo.GetByIdAsync(id);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            // 2. Convert TimeSpan → TimeOnly
            var openTime = TimeOnly.FromTimeSpan(dto.OpenTime);
            var closeTime = TimeOnly.FromTimeSpan(dto.CloseTime);

            // 3. Validate open_time < close_time
            if (openTime >= closeTime)
                throw new AppException(400,
                    "Giờ mở cửa phải nhỏ hơn giờ đóng cửa", ErrorCodes.BadRequest);

            // 4. Check tên unique — bỏ qua chính nó
            var exists = await _repo.ExistsByNameAsync(dto.Name, id);
            if (exists)
                throw new AppException(409, "Tên chi nhánh đã tồn tại", ErrorCodes.Conflict);

            // 5. Update
            branch.Name = dto.Name.Trim();
            branch.Address = dto.Address.Trim();
            branch.Phone = dto.Phone?.Trim();
            branch.AvatarUrl = dto.AvatarUrl?.Trim();
            branch.Latitude = dto.Latitude;
            branch.Longitude = dto.Longitude;
            branch.OpenTime = openTime;
            branch.CloseTime = closeTime;
            branch.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _repo.UpdateAsync(branch);
            }
            catch (DbUpdateException)
            {
                throw new AppException(409, "Tên chi nhánh đã tồn tại", ErrorCodes.Conflict);
            }

            var updatedResult = await _repo.GetWithManagerAsync(id);
            return MapToDto(updatedResult!.Value.Branch, updatedResult.Value.ManagerAssignment);
        }

        // tạm khóa chi nhánh 
        public async Task SuspendAsync(Guid id)
        {
            var branch = await _repo.GetByIdAsync(id);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            if (branch.Status == BranchStatus.SUSPENDED)
                throw new AppException(400,
                    "Chi nhánh đã bị tạm khóa", ErrorCodes.BadRequest);

            // Check booking active
            var hasActiveBookings = await _repo.HasActiveBookingsAsync(id);
            if (hasActiveBookings)
                throw new AppException(400,
                    "Chi nhánh đang có đơn chưa hoàn thành, không thể tạm khóa",
                    ErrorCodes.ResourceInUse);

            branch.Status = BranchStatus.SUSPENDED;
            branch.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(branch);
        }

        // mở lại chi nhánh từ trạng thái tạm khóa
        public async Task ActivateAsync(Guid id)
        {
            var branch = await _repo.GetByIdAsync(id);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            if (branch.Status == BranchStatus.ACTIVE)
                throw new AppException(400,
                    "Chi nhánh đang hoạt động bình thường", ErrorCodes.BadRequest);

            if (branch.Status == BranchStatus.INACTIVE)
                throw new AppException(400,
                    "Chi nhánh đã bị vô hiệu hóa, không thể mở lại", ErrorCodes.BadRequest);

            branch.Status = BranchStatus.ACTIVE;
            branch.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(branch);
        }

        // vô hiệu hóa chi nhánh, không xóa hẳn để giữ lịch sử booking
        public async Task DeleteAsync(Guid id)
        {
            var branch = await _repo.GetByIdAsync(id);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            // Không cho xóa nếu có đơn active
            var hasActiveBookings = await _repo.HasActiveBookingsAsync(id);
            if (hasActiveBookings)
                throw new AppException(400,
                    "Chi nhánh đang có đơn chưa hoàn thành, không thể xóa",
                    ErrorCodes.ResourceInUse);

            branch.Status = BranchStatus.INACTIVE;
            branch.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(branch);
        }

        // Lấy danh sách TẤT CẢ loại sân (kèm trạng thái bật/tắt và số lượng sân) tại chi nhánh
        public async Task<List<BranchCourtTypeDto>> GetCourtTypesAsync(Guid branchId)
        {
            // Kiểm tra branch tồn tại
            var branch = await _repo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            return await _repo.GetAllCourtTypeDetailsAsync(branchId);
        }

        // Thêm loại sân vào chi nhánh (bật loại sân)
        public async Task<BranchCourtTypeDto> AddCourtTypeAsync(Guid branchId,AddCourtTypeToBranchDto dto, Guid currentUserId,string currentUserRole)
        {
            // 1. Tìm branch
            var branch = await _repo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);
            // Check chi nhánh đang hoạt động
            if (branch.Status != BranchStatus.ACTIVE) 
                throw new AppException(400,
                    "Chi nhánh không đang hoạt động, không thể thêm loại sân",
                    ErrorCodes.BadRequest);

            // 2. MANAGER chỉ thao tác được chi nhánh mình
            if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
            {
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(
                    currentUserId, branchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Bạn không có quyền thao tác chi nhánh này",
                        ErrorCodes.Forbidden);
            }

            // 3. Tìm court type
            var courtType = await _courtTypeRepo.GetByIdAsync(dto.CourtTypeId);
            if (courtType == null)
                throw new AppException(404, "Không tìm thấy loại sân", ErrorCodes.NotFound);

            if (courtType.Status != CourtTypeStatus.ACTIVE)
                throw new AppException(400,
                    "Loại sân không còn hoạt động", ErrorCodes.BadRequest);

            // 4. Kiểm tra đã bật chưa
            var existing = await _repo.GetBranchCourtTypeAsync(branchId, dto.CourtTypeId);
            if (existing != null)
            {
                // Đã tồn tại nhưng đang tắt → bật lại
                if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    await _repo.UpdateBranchCourtTypeAsync(existing);
                    
                    // Đếm số sân active của court type này trong chi nhánh
                    var courtCount = await _courtRepo.GetAllByBranchAsync(branchId, true)
                        .ContinueWith(t => t.Result.Count(c => c.CourtTypeId == dto.CourtTypeId && c.Status != CourtStatus.INACTIVE));
                    
                    return MapToCourtTypeDto(existing, courtCount);
                }
                // Đang bật rồi → conflict
                throw new AppException(409,
                    "Loại sân này đã được bật tại chi nhánh", ErrorCodes.Conflict);
            }

            // 5. Tạo mới
            var branchCourtType = new BranchCourtType
            {
                BranchId = branchId,
                CourtTypeId = dto.CourtTypeId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var created = await _repo.AddCourtTypeAsync(branchCourtType);

            var reloaded = await _repo.GetBranchCourtTypeAsync(branchId, dto.CourtTypeId)
    ?? throw new AppException(500, "Lỗi khi tạo loại sân", ErrorCodes.InternalError);

            // Đếm số sân active của court type này trong chi nhánh
            var reloadedCourtCount = await _courtRepo.GetAllByBranchAsync(branchId, true)
                .ContinueWith(t => t.Result.Count(c => c.CourtTypeId == dto.CourtTypeId && c.Status != CourtStatus.INACTIVE));

            return MapToCourtTypeDto(reloaded, reloadedCourtCount);
        }

        // Xóa loại sân khỏi chi nhánh (tắt loại sân)
        public async Task RemoveCourtTypeAsync(Guid branchId,Guid courtTypeId,Guid currentUserId,string currentUserRole)
        {
            // 1. Tìm branch
            var branch = await _repo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            // 2. MANAGER chỉ thao tác chi nhánh mình
            if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
            {
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(
                    currentUserId, branchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Bạn không có quyền thao tác chi nhánh này",
                        ErrorCodes.Forbidden);
            }

            // 3. Tìm BranchCourtType
            var branchCourtType = await _repo.GetBranchCourtTypeAsync(branchId, courtTypeId);
            if (branchCourtType == null)
                throw new AppException(404,
                    "Loại sân không tồn tại tại chi nhánh này", ErrorCodes.NotFound);

            if (!branchCourtType.IsActive)
                throw new AppException(400,
                    "Loại sân này đã được tắt trước đó", ErrorCodes.BadRequest);

            // 4. Check có sân nào đang dùng loại sân này không
            var hasCourts = await _repo.HasCourtsWithTypeAsync(branchId, courtTypeId);
            if (hasCourts)
                throw new AppException(400,
                    "Loại sân đang được sử dụng, không thể bỏ",
                    ErrorCodes.ResourceInUse);

            // 5. Soft delete
            branchCourtType.IsActive = false;
            await _repo.UpdateBranchCourtTypeAsync(branchCourtType);
        }

        // Lấy danh sách dịch vụ được cung cấp tại chi nhánh, kèm giá (giá chi nhánh nếu có, không thì giá mặc định)
        public async Task<List<BranchServiceDto>> GetServicesAsync(Guid branchId)
        {
            var branch = await _repo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var services = await _repo.GetServicesAsync(branchId);
            return services.Select(MapToServiceDto).ToList();
        }

        // Thêm dịch vụ vào chi nhánh (bật dịch vụ), có thể kèm giá override
        public async Task<BranchServiceDto> AddServiceAsync(Guid branchId,AddServiceToBranchDto dto,Guid currentUserId,string currentUserRole)
        {
            // 1. Tìm branch
            var branch = await _repo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);
            // Check chi nhánh đang hoạt động
            if (branch.Status != BranchStatus.ACTIVE) 
                throw new AppException(400,
                    "Chi nhánh không đang hoạt động, không thể thêm dịch vụ",
                    ErrorCodes.BadRequest);

            // 2. MANAGER chỉ thao tác chi nhánh mình
            if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
            {
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(
                    currentUserId, branchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Bạn không có quyền thao tác chi nhánh này",
                        ErrorCodes.Forbidden);
            }

            // 3. Tìm service
            var service = await _serviceRepo.GetByIdAsync(dto.ServiceId);
            if (service == null)
                throw new AppException(404, "Không tìm thấy dịch vụ", ErrorCodes.NotFound);

            if (service.Status != ServiceStatus.ACTIVE)
                throw new AppException(400,
                    "Dịch vụ không còn hoạt động", ErrorCodes.BadRequest);

            // Giá áp dụng = giá override nếu có, không thì dùng giá mặc định
            var effectivePrice = dto.Price ?? service.DefaultPrice;

            // 4. Kiểm tra đã bật chưa
            var existing = await _repo.GetBranchServiceAsync(branchId, dto.ServiceId);
            if (existing != null)
            {
                if (existing.Status == BranchServiceStatus.ENABLED)
                    throw new AppException(409,
                        "Dịch vụ này đã được bật tại chi nhánh", ErrorCodes.Conflict);

                // DISABLED → bật lại + update giá
                await _repo.UpdateBranchServiceAsync(
                    existing.Id, effectivePrice, BranchServiceStatus.ENABLED);

                existing.Price = effectivePrice;
                existing.Status = BranchServiceStatus.ENABLED;
                existing.Service = service;
                return MapToServiceDto(existing);
            }

            // 5. Tạo mới
            var branchService = new Models.Entities.BranchService
            {
                BranchId = branchId,
                ServiceId = dto.ServiceId,
                Price = effectivePrice,
                Status = BranchServiceStatus.ENABLED,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await _repo.AddServiceAsync(branchService);

            // Reload để đảm bảo navigation properties
            var reloaded = await _repo.GetBranchServiceAsync(branchId, dto.ServiceId)
                ?? throw new AppException(500,
                    "Lỗi khi tạo dịch vụ", ErrorCodes.InternalError);

            return MapToServiceDto(reloaded);
        }

        // Cập nhật giá dịch vụ của chi nhánh, chỉ update giá và bật lại nếu đang tắt
        public async Task<BranchServiceDto> UpdateServicePriceAsync(Guid branchId, Guid serviceId,UpdateBranchServiceDto dto,Guid currentUserId,string currentUserRole)
        {
            // 1. Validate branch + quyền
            await ValidateBranchAccessAsync(branchId, currentUserId, currentUserRole);

            // 2. Kiểm tra tình trạng chi nhánh và tìm BranchService
            var branch = await _repo.GetByIdAsync(branchId);
            if (branch!.Status != BranchStatus.ACTIVE)
                throw new AppException(400,
                    "Chi nhánh không đang hoạt động, không thể cập nhật dịch vụ",
                    ErrorCodes.BadRequest);
            var branchService = await _repo.GetBranchServiceAsync(branchId, serviceId);
            if (branchService == null)
                throw new AppException(404,
                    "Dịch vụ không tồn tại tại chi nhánh này", ErrorCodes.NotFound);

            if (branchService.Status == BranchServiceStatus.DISABLED)
                throw new AppException(400,
                    "Dịch vụ đang tắt, hãy bật lại trước khi cập nhật giá",
                    ErrorCodes.BadRequest);

            // 3. Update giá
            await _repo.UpdateBranchServiceAsync(
                branchService.Id, dto.Price, BranchServiceStatus.ENABLED);

            branchService.Price = dto.Price;
            return MapToServiceDto(branchService);
        }

        // Tắt dịch vụ tại chi nhánh, không xóa hẳn để giữ lịch sử booking
        public async Task DisableServiceAsync(Guid branchId,Guid serviceId,Guid currentUserId, string currentUserRole)
        {
            // 1. Validate branch + quyền
            await ValidateBranchAccessAsync(branchId, currentUserId, currentUserRole);

            // 2. Tìm BranchService
            var branchService = await _repo.GetBranchServiceAsync(branchId, serviceId);
            if (branchService == null)
                throw new AppException(404,
                    "Dịch vụ không tồn tại tại chi nhánh này", ErrorCodes.NotFound);

            if (branchService.Status == BranchServiceStatus.DISABLED)
                throw new AppException(400,
                    "Dịch vụ này đã được tắt trước đó", ErrorCodes.BadRequest);

            // 3. Tắt dịch vụ — không cần check đơn đang mở vì đã snapshot
            await _repo.UpdateBranchServiceAsync(
                branchService.Id, branchService.Price, BranchServiceStatus.DISABLED);
        }

        // map data sang service dto
        private static BranchServiceDto MapToServiceDto(Models.Entities.BranchService bs) => new()
        {
            Id = bs.Id,
            ServiceId = bs.ServiceId,
            ServiceName = bs.Service.Name,
            Description = bs.Service.Description,
            Unit = bs.Service.Unit,
            DefaultPrice = bs.Service.DefaultPrice,
            // Giá thực tế = giá chi nhánh nếu có, không thì dùng giá mặc định
            EffectivePrice = bs.Price != bs.Service.DefaultPrice ? bs.Price : bs.Service.DefaultPrice,
            Status = bs.Status,
            CreatedAt = bs.CreatedAt,
            UpdatedAt = bs.UpdatedAt
        };


        // map data sang dto 
        private static BranchDto MapToDto(Branch b, UserBranch? manager = null) => new()
        {
            Id = b.Id,
            Name = b.Name,
            Address = b.Address,
            Latitude = b.Latitude,
            Longitude = b.Longitude,
            Phone = b.Phone,
            AvatarUrl = b.AvatarUrl,
            OpenTime = b.OpenTime.ToTimeSpan(),
            CloseTime = b.CloseTime.ToTimeSpan(),
            Status = b.Status,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt,
            ManagerId = manager?.UserId,
            ManagerName = manager?.User?.FullName
        };

        // map data sang court type dto
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
}
