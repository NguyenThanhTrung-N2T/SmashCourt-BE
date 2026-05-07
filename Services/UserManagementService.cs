using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.UserManagement;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

public class UserManagementService : IUserManagementService
{
    private readonly IUserRepository _userRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly IBranchRepository _branchRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly EmailService _emailService;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IUserRepository userRepo,
        IUserBranchRepository userBranchRepo,
        IBranchRepository branchRepo,
        IRefreshTokenRepository refreshTokenRepo,
        EmailService emailService,
        ILogger<UserManagementService> logger)
    {
        _userRepo = userRepo;
        _userBranchRepo = userBranchRepo;
        _branchRepo = branchRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _emailService = emailService;
        _logger = logger;
    }

    #region Helper Methods

    /// <summary>
    /// Kiểm tra quyền truy cập user và trả về User entity để tránh double fetch
    /// OWNER: Có thể truy cập tất cả users
    /// BRANCH_MANAGER: Chỉ có thể truy cập STAFF trong chi nhánh của mình
    /// </summary>
    /// <returns>User entity nếu có quyền truy cập, null nếu OWNER (caller cần fetch riêng)</returns>
    private async Task<User?> ValidateAccessToUserAsync(Guid targetUserId, Guid currentUserId, string currentUserRole)
    {
        // OWNER có full access - return null để caller tự fetch với context phù hợp
        if (currentUserRole == UserRole.OWNER.ToString())
            return null;

        // BRANCH_MANAGER chỉ có thể truy cập STAFF trong chi nhánh của mình
        if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
        {
            var targetUser = await _userRepo.GetUserByIdWithBranchAsync(targetUserId);
            if (targetUser == null)
                throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

            // Không thể thao tác với OWNER hoặc BRANCH_MANAGER khác
            if (targetUser.Role != UserRole.STAFF)
                throw new AppException(403, "Bạn không có quyền thao tác với người dùng này", ErrorCodes.Forbidden);

            // Kiểm tra target user có trong chi nhánh của manager không
            var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
            if (managerBranch == null)
                throw new AppException(403, "Bạn chưa được gán chi nhánh", ErrorCodes.Forbidden);

            var targetUserBranch = targetUser.UserBranches.FirstOrDefault(ub => ub.IsActive);
            if (targetUserBranch == null || targetUserBranch.BranchId != managerBranch.BranchId)
                throw new AppException(403, "Bạn chỉ có thể thao tác với nhân viên trong chi nhánh của mình", ErrorCodes.Forbidden);

            return targetUser; // Trả về user đã fetch để tránh double fetch
        }

        throw new AppException(403, "Bạn không có quyền thực hiện thao tác này", ErrorCodes.Forbidden);
    }

    /// <summary>
    /// Force role dựa trên currentUserRole (KHÔNG trust input từ client)
    /// OWNER: Có thể tạo STAFF hoặc BRANCH_MANAGER
    /// BRANCH_MANAGER: Chỉ có thể tạo STAFF
    /// </summary>
    private UserRole ForceRoleBasedOnCurrentUser(UserRole requestedRole, string currentUserRole)
    {
        if (currentUserRole == UserRole.OWNER.ToString())
        {
            // OWNER có thể tạo STAFF hoặc BRANCH_MANAGER
            if (requestedRole == UserRole.STAFF || requestedRole == UserRole.BRANCH_MANAGER)
                return requestedRole;

            throw new AppException(400, "Role không hợp lệ. Chỉ có thể tạo STAFF hoặc BRANCH_MANAGER", ErrorCodes.InvalidRole);
        }

        if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
        {
            // BRANCH_MANAGER chỉ có thể tạo STAFF
            return UserRole.STAFF;
        }

        throw new AppException(403, "Bạn không có quyền tạo người dùng", ErrorCodes.Forbidden);
    }

    /// <summary>
    /// Map User entity sang UserDto
    /// </summary>
    private UserDto MapToUserDto(User user)
    {
        var activeBranch = user.UserBranches.FirstOrDefault(ub => ub.IsActive);

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Phone = user.Phone,
            AvatarUrl = user.AvatarUrl,
            Role = user.Role.ToString(),
            Status = user.Status.ToString(),
            CreatedAt = user.CreatedAt,
            CurrentBranch = activeBranch != null ? new BranchInfoDto
            {
                Id = activeBranch.BranchId,
                Name = activeBranch.Branch.Name,
                Role = activeBranch.Role.ToString()
            } : null
        };
    }

    /// <summary>
    /// Map User entity sang UserDetailDto
    /// </summary>
    private async Task<UserDetailDto> MapToUserDetailDtoAsync(User user)
    {
        var activeBranch = user.UserBranches.FirstOrDefault(ub => ub.IsActive);

        LockInfoDto? lockInfo = null;
        if (user.Status == UserStatus.LOCKED && user.LockedBy.HasValue)
        {
            var lockedByUser = await _userRepo.GetUserByIdAsync(user.LockedBy.Value);
            lockInfo = new LockInfoDto
            {
                Reason = user.LockReason,
                LockedAt = user.LockedAt,
                LockedBy = user.LockedBy,
                LockedByName = lockedByUser?.FullName
            };
        }

        return new UserDetailDto
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Phone = user.Phone,
            AvatarUrl = user.AvatarUrl,
            Role = user.Role.ToString(),
            Status = user.Status.ToString(),
            IsEmailVerified = user.IsEmailVerified,
            MustChangePassword = user.MustChangePassword,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LockInfo = lockInfo,
            CurrentBranch = activeBranch != null ? new BranchInfoDto
            {
                Id = activeBranch.BranchId,
                Name = activeBranch.Branch.Name,
                Role = activeBranch.Role.ToString()
            } : null
        };
    }

    /// <summary>
    /// Invalidate tất cả refresh tokens của user (dùng khi lock/deactivate)
    /// </summary>
    private async Task InvalidateAllUserTokensAsync(Guid userId)
    {
        try
        {
            await _refreshTokenRepo.RevokeAllByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke tokens for user {UserId}", userId);
            // Không throw exception - tokens sẽ tự hết hạn
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Lấy danh sách users với filter và phân trang
    /// </summary>
    public async Task<PagedResult<UserDto>> GetUsersAsync(UserListQuery query, Guid currentUserId, string currentUserRole)
    {
        // BRANCH_MANAGER chỉ xem STAFF trong chi nhánh của mình
        if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
        {
            var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
            if (managerBranch == null)
                throw new AppException(403, "Bạn chưa được gán chi nhánh", ErrorCodes.Forbidden);

            // Force filter theo chi nhánh và role = STAFF
            query.BranchId = managerBranch.BranchId;
            query.Role = UserRole.STAFF;
        }

        var result = await _userRepo.GetUsersAsync(query);

        return new PagedResult<UserDto>
        {
            Items = result.Items.Select(MapToUserDto).ToList(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalItems = result.TotalItems
        };
    }

    /// <summary>
    /// Lấy thông tin chi tiết user
    /// </summary>
    public async Task<UserDetailDto> GetUserByIdAsync(Guid userId, Guid currentUserId, string currentUserRole)
    {
        var validatedUser = await ValidateAccessToUserAsync(userId, currentUserId, currentUserRole);

        // Nếu OWNER (validatedUser = null) hoặc cần fetch lại
        var user = validatedUser ?? await _userRepo.GetUserByIdWithBranchAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        return await MapToUserDetailDtoAsync(user);
    }

    /// <summary>
    /// Tạo user mới (STAFF hoặc BRANCH_MANAGER)
    /// </summary>
    public async Task<UserDetailDto> CreateUserAsync(CreateUserDto dto, Guid currentUserId, string currentUserRole)
    {
        // 1. Force role dựa trên currentUserRole
        var actualRole = ForceRoleBasedOnCurrentUser(dto.RequestedRole, currentUserRole);

        // 2. Validate branch
        var branch = await _branchRepo.GetByIdAsync(dto.BranchId);
        if (branch == null)
            throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.BranchNotFound);

        // BRANCH_MANAGER chỉ có thể tạo user trong chi nhánh của mình
        if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
        {
            var managerBranch = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
            if (managerBranch == null || managerBranch.BranchId != dto.BranchId)
                throw new AppException(403, "Bạn chỉ có thể tạo nhân viên trong chi nhánh của mình", ErrorCodes.Forbidden);
        }

        // 3. Kiểm tra email đã tồn tại chưa (case-insensitive)
        var normalizedEmail = dto.Email.Trim().ToLower();
        if (await _userRepo.IsEmailExistsAsync(normalizedEmail))
            throw new AppException(409, "Email đã được sử dụng", ErrorCodes.UserAlreadyExists);

        // 4. Tạo password (tự động hoặc từ input)
        var password = string.IsNullOrWhiteSpace(dto.TemporaryPassword)
            ? PasswordHelper.GenerateRandomPassword()
            : dto.TemporaryPassword;

        // 5. Tạo user
        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12),
            FullName = dto.FullName.Trim(),
            Phone = dto.Phone?.Trim(),
            Role = actualRole,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = true, // User được tạo bởi admin → tự động verify
            MustChangePassword = true, // Bắt buộc đổi password lần đầu đăng nhập
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            user = await _userRepo.CreateUserAsync(user);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to create user with email {Email}", normalizedEmail);
            throw new AppException(409, "Email đã được sử dụng", ErrorCodes.UserAlreadyExists);
        }

        // 6. Gán chi nhánh
        var userBranchRole = actualRole == UserRole.BRANCH_MANAGER
            ? UserBranchRole.MANAGER
            : UserBranchRole.STAFF;

        var userBranch = new UserBranch
        {
            UserId = user.Id,
            BranchId = dto.BranchId,
            Role = userBranchRole,
            IsActive = true,
            AssignedAt = DateTime.UtcNow
        };

        await _userBranchRepo.CreateAsync(userBranch);

        // 7. Gửi email chào mừng với thông tin đăng nhập
        try
        {
            await _emailService.SendWelcomeEmailAsync(user.Email, user.FullName, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to {Email}", user.Email);
            // Không throw exception - user đã được tạo thành công
        }

        // 8. Load lại user với branch info
        user = await _userRepo.GetUserByIdWithBranchAsync(user.Id);
        return await MapToUserDetailDtoAsync(user!);
    }

    /// <summary>
    /// Cập nhật thông tin user
    /// </summary>
    public async Task<UserDetailDto> UpdateUserAsync(Guid userId, UpdateUserDto dto, Guid currentUserId, string currentUserRole)
    {
        var validatedUser = await ValidateAccessToUserAsync(userId, currentUserId, currentUserRole);

        // Nếu OWNER (validatedUser = null) hoặc cần fetch lại
        var user = validatedUser ?? await _userRepo.GetUserByIdWithBranchAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        // Cập nhật thông tin
        user.FullName = dto.FullName.Trim();
        user.Phone = dto.Phone?.Trim();
        user.AvatarUrl = dto.AvatarUrl;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(user);

        return await MapToUserDetailDtoAsync(user);
    }

    /// <summary>
    /// Khóa user (status = LOCKED)
    /// </summary>
    public async Task LockUserAsync(Guid userId, LockUserDto dto, Guid currentUserId, string currentUserRole)
    {
        // Không thể khóa chính mình
        if (userId == currentUserId)
            throw new AppException(400, "Bạn không thể khóa chính mình", ErrorCodes.CannotLockSelf);

        var validatedUser = await ValidateAccessToUserAsync(userId, currentUserId, currentUserRole);

        // Nếu OWNER (validatedUser = null) hoặc cần fetch lại
        var user = validatedUser ?? await _userRepo.GetUserByIdWithBranchAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        // Kiểm tra đã bị khóa chưa
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(400, "Người dùng đã bị khóa", ErrorCodes.UserAlreadyLocked);

        // Kiểm tra nếu là BRANCH_MANAGER cuối cùng trong chi nhánh
        if (user.Role == UserRole.BRANCH_MANAGER)
        {
            var activeBranch = user.UserBranches.FirstOrDefault(ub => ub.IsActive);
            if (activeBranch != null)
            {
                var activeManagerCount = await _userRepo.CountActiveBranchManagersAsync(activeBranch.BranchId);
                if (activeManagerCount <= 1)
                    throw new AppException(400, "Không thể khóa quản lý cuối cùng của chi nhánh", ErrorCodes.LastBranchManager);
            }
        }

        // Khóa user
        user.Status = UserStatus.LOCKED;
        user.LockReason = dto.Reason;
        user.LockedAt = DateTime.UtcNow;
        user.LockedBy = currentUserId;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(user);

        // Invalidate tất cả refresh tokens
        await InvalidateAllUserTokensAsync(userId);
    }

    /// <summary>
    /// Mở khóa user (status = ACTIVE)
    /// </summary>
    public async Task UnlockUserAsync(Guid userId, Guid currentUserId, string currentUserRole)
    {
        var validatedUser = await ValidateAccessToUserAsync(userId, currentUserId, currentUserRole);

        // Reuse validated user nếu có (BRANCH_MANAGER case), hoặc fetch mới (OWNER case)
        var user = validatedUser ?? await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        // Kiểm tra có bị khóa không
        if (user.Status != UserStatus.LOCKED)
            throw new AppException(400, "Người dùng không bị khóa", ErrorCodes.UserNotLocked);

        // Mở khóa
        user.Status = UserStatus.ACTIVE;
        user.LockReason = null;
        user.LockedAt = null;
        user.LockedBy = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(user);
    }

    /// <summary>
    /// Đánh dấu user là INACTIVE (nghỉ việc, archived)
    /// </summary>
    public async Task DeactivateUserAsync(Guid userId, Guid currentUserId, string currentUserRole)
    {
        // Không thể deactivate chính mình
        if (userId == currentUserId)
            throw new AppException(400, "Bạn không thể vô hiệu hóa chính mình", ErrorCodes.CannotLockSelf);

        var validatedUser = await ValidateAccessToUserAsync(userId, currentUserId, currentUserRole);

        // Nếu OWNER (validatedUser = null) hoặc cần fetch lại
        var user = validatedUser ?? await _userRepo.GetUserByIdWithBranchAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        // Kiểm tra đã inactive chưa
        if (user.Status == UserStatus.INACTIVE)
            throw new AppException(400, "Người dùng đã bị vô hiệu hóa", ErrorCodes.UserAlreadyInactive);

        // Kiểm tra nếu là BRANCH_MANAGER cuối cùng trong chi nhánh
        if (user.Role == UserRole.BRANCH_MANAGER)
        {
            var activeBranch = user.UserBranches.FirstOrDefault(ub => ub.IsActive);
            if (activeBranch != null)
            {
                var activeManagerCount = await _userRepo.CountActiveBranchManagersAsync(activeBranch.BranchId);
                if (activeManagerCount <= 1)
                    throw new AppException(400, "Không thể vô hiệu hóa quản lý cuối cùng của chi nhánh", ErrorCodes.LastBranchManager);
            }
        }

        // Deactivate user
        user.Status = UserStatus.INACTIVE;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(user);

        // End assignment trong chi nhánh
        var userActiveBranch = user.UserBranches.FirstOrDefault(ub => ub.IsActive);
        if (userActiveBranch != null)
        {
            userActiveBranch.IsActive = false;
            userActiveBranch.EndedAt = DateTime.UtcNow;
            await _userBranchRepo.UpdateAsync(userActiveBranch);
        }

        // Invalidate tất cả refresh tokens
        await InvalidateAllUserTokensAsync(userId);
    }

    /// <summary>
    /// Kích hoạt lại user từ INACTIVE về ACTIVE
    /// </summary>
    public async Task ActivateUserAsync(Guid userId, Guid currentUserId, string currentUserRole)
    {
        var validatedUser = await ValidateAccessToUserAsync(userId, currentUserId, currentUserRole);

        // CRITICAL FIX: Phải dùng GetUserByIdWithAllBranchesAsync để load cả inactive branches
        // GetUserByIdWithBranchAsync chỉ load active branches → không tìm được inactive branch để restore
        var user = validatedUser ?? await _userRepo.GetUserByIdWithAllBranchesAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        // Kiểm tra có inactive không
        if (user.Status != UserStatus.INACTIVE)
            throw new AppException(400, "Người dùng không bị vô hiệu hóa", ErrorCodes.UserNotInactive);

        // Activate user
        user.Status = UserStatus.ACTIVE;
        user.UpdatedAt = DateTime.UtcNow;

        // Kiểm tra và restore branch nếu có branch bị deactivate
        var inactiveBranch = user.UserBranches.FirstOrDefault(ub => !ub.IsActive && ub.EndedAt != null);
        if (inactiveBranch != null)
        {
            // Restore branch assignment
            inactiveBranch.IsActive = true;
            inactiveBranch.EndedAt = null;
            _logger.LogInformation("Restored branch {BranchId} for user {UserId}", inactiveBranch.BranchId, userId);
        }
        else
        {
            _logger.LogWarning("User {UserId} activated but has no branch assignment. Admin needs to assign branch manually.", userId);
        }

        await _userRepo.UpdateUserAsync(user);
    }

    /// <summary>
    /// Cập nhật chi nhánh của user
    /// </summary>
    public async Task UpdateUserBranchAsync(Guid userId, UpdateUserBranchDto dto, Guid currentUserId, string currentUserRole)
    {
        // Chỉ OWNER mới có quyền
        if (currentUserRole != UserRole.OWNER.ToString())
            throw new AppException(403, "Chỉ OWNER mới có quyền chuyển chi nhánh", ErrorCodes.Forbidden);

        var user = await _userRepo.GetUserByIdWithBranchAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        // Validate branch mới
        var newBranch = await _branchRepo.GetByIdAsync(dto.NewBranchId);
        if (newBranch == null)
            throw new AppException(404, "Không tìm thấy chi nhánh mới", ErrorCodes.BranchNotFound);

        // Lấy assignment hiện tại
        var currentAssignment = user.UserBranches.FirstOrDefault(ub => ub.IsActive);
        if (currentAssignment == null)
            throw new AppException(400, "Người dùng chưa được gán chi nhánh", ErrorCodes.UserHasNoBranch);

        // Kiểm tra nếu đã ở chi nhánh đích → không cần chuyển
        if (currentAssignment.BranchId == dto.NewBranchId)
            throw new AppException(400, "Người dùng đã thuộc chi nhánh này", ErrorCodes.BadRequest);

        // Kiểm tra nếu là BRANCH_MANAGER cuối cùng trong chi nhánh cũ
        if (user.Role == UserRole.BRANCH_MANAGER)
        {
            var activeManagerCount = await _userRepo.CountActiveBranchManagersAsync(currentAssignment.BranchId);
            if (activeManagerCount <= 1)
                throw new AppException(400, "Không thể chuyển quản lý cuối cùng của chi nhánh", ErrorCodes.LastBranchManager);
        }

        // End assignment cũ
        currentAssignment.IsActive = false;
        currentAssignment.EndedAt = DateTime.UtcNow;
        await _userBranchRepo.UpdateAsync(currentAssignment);

        // Tạo assignment mới
        var newAssignment = new UserBranch
        {
            UserId = userId,
            BranchId = dto.NewBranchId,
            Role = currentAssignment.Role, // Giữ nguyên role
            IsActive = true,
            AssignedAt = DateTime.UtcNow
        };

        await _userBranchRepo.CreateAsync(newAssignment);

        // Update user
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);
    }

    /// <summary>
    /// Reset password cho user
    /// </summary>
    public async Task<string> ResetPasswordAsync(Guid userId, ResetPasswordDto dto, Guid currentUserId, string currentUserRole)
    {
        // Không thể reset password chính mình
        if (userId == currentUserId)
            throw new AppException(400, "Bạn không thể reset password cho chính mình", ErrorCodes.CannotResetOwnPassword);

        await ValidateAccessToUserAsync(userId, currentUserId, currentUserRole);

        // Luôn fetch mới vì ResetPassword không cần branch info
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.UserNotFound);

        // SECURITY FIX: Không cho phép reset password của CUSTOMER
        // CUSTOMER phải tự reset qua forgot-password flow
        if (user.Role == UserRole.CUSTOMER)
            throw new AppException(403, "Không thể reset password của khách hàng. Khách hàng phải tự reset qua chức năng quên mật khẩu", ErrorCodes.Forbidden);

        // Tạo password mới (tự động hoặc từ input)
        var newPassword = string.IsNullOrWhiteSpace(dto.NewPassword)
            ? PasswordHelper.GenerateRandomPassword()
            : dto.NewPassword;

        // Update password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, 12);
        user.MustChangePassword = true; // Bắt buộc đổi password lần đầu đăng nhập
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(user);

        // Invalidate tất cả refresh tokens
        await InvalidateAllUserTokensAsync(userId);

        // Gửi email thông báo password mới
        try
        {
            await _emailService.SendPasswordResetByAdminAsync(user.Email, user.FullName, newPassword);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
            // Không throw exception - password đã được reset thành công
        }

        return "Reset password thành công. Mật khẩu mới đã được gửi qua email.";
    }

    #endregion
}
