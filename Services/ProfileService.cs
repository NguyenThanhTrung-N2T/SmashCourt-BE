using BCrypt.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Loyalty;
using SmashCourt_BE.DTOs.Profile;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Service quản lý profile và session của user hiện tại
/// </summary>
public class ProfileService : IProfileService
{
    private readonly IUserRepository _userRepo;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly ICustomerLoyaltyRepository _customerLoyaltyRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly ILoyaltyTierRepository _loyaltyTierRepo;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(
        IUserRepository userRepo,
        IRefreshTokenRepository refreshTokenRepo,
        ICustomerLoyaltyRepository customerLoyaltyRepo,
        IUserBranchRepository userBranchRepo,
        ILoyaltyTierRepository loyaltyTierRepo,
        IConfiguration configuration,
        ILogger<ProfileService> logger)
    {
        _userRepo = userRepo;
        _refreshTokenRepo = refreshTokenRepo;
        _customerLoyaltyRepo = customerLoyaltyRepo;
        _userBranchRepo = userBranchRepo;
        _loyaltyTierRepo = loyaltyTierRepo;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Lấy thông tin profile của user hiện tại
    /// - CUSTOMER: bao gồm thông tin Loyalty
    /// - STAFF/MANAGER: bao gồm thông tin Branch
    /// </summary>
    public async Task<UserProfileDto> GetMyProfileAsync(Guid userId)
    {
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy user", ErrorCodes.UserNotFound);

        var profile = new UserProfileDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            AvatarUrl = user.AvatarUrl,
            Role = user.Role.ToString(),
            Status = user.Status.ToString(),
            Is2faEnabled = user.Is2faEnabled,
            MustChangePassword = user.MustChangePassword,
            CreatedAt = user.CreatedAt
        };

        // Nếu là CUSTOMER → lấy thông tin Loyalty
        if (user.Role == UserRole.CUSTOMER)
        {
            var loyalty = await _customerLoyaltyRepo.GetByUserIdAsync(userId);
            if (loyalty != null)
            {
                var tier = await _loyaltyTierRepo.GetLoyaltyTierByIdAsync(loyalty.TierId);
                if (tier != null)
                {
                    // Lấy tier tiếp theo
                    var nextTier = await _loyaltyTierRepo.GetNextTierAsync(tier.MinPoints);

                    profile.Loyalty = new LoyaltyInfo
                    {
                        TierName = tier.Name,
                        DiscountRate = tier.DiscountRate,
                        CurrentPoints = loyalty.TotalPoints,
                        NextTierPoints = nextTier?.MinPoints ?? loyalty.TotalPoints,
                        NextTierName = nextTier?.Name,
                        ProgressPercentage = CalculateProgressPercentage(loyalty.TotalPoints, tier.MinPoints, nextTier?.MinPoints),
                        PointsToNextTier = nextTier != null ? nextTier.MinPoints - loyalty.TotalPoints : 0
                    };
                }
            }
        }

        // Nếu là STAFF hoặc BRANCH_MANAGER → lấy thông tin Branch
        if (user.Role == UserRole.STAFF || user.Role == UserRole.BRANCH_MANAGER)
        {
            var userBranch = await _userBranchRepo.GetActiveByUserIdAsync(userId);
            if (userBranch?.Branch != null)
            {
                profile.Branch = new BranchInfo
                {
                    Id = userBranch.Branch.Id,
                    Name = userBranch.Branch.Name,
                    Role = userBranch.Role.ToString()
                };
            }
        }

        return profile;
    }

    /// <summary>
    /// Cập nhật thông tin profile của user hiện tại
    /// Chỉ cho phép cập nhật: fullName, phone, avatarUrl
    /// </summary>
    public async Task UpdateMyProfileAsync(Guid userId, UpdateProfileDto dto)
    {
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy user", ErrorCodes.UserNotFound);

        // Chỉ cập nhật các trường được phép
        user.FullName = dto.FullName;
        user.Phone = dto.Phone;
        user.AvatarUrl = dto.AvatarUrl;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(user);

        _logger.LogInformation("User {UserId} đã cập nhật profile", userId);
    }

    /// <summary>
    /// Đổi mật khẩu cho user hiện tại
    /// - Xác thực mật khẩu hiện tại
    /// - Validate mật khẩu mới
    /// - Thu hồi TẤT CẢ refresh tokens sau khi đổi
    /// - OAuth users không thể đổi mật khẩu
    /// </summary>
    public async Task ChangePasswordAsync(Guid userId, SelfChangePasswordDto dto)
    {
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Không tìm thấy user", ErrorCodes.UserNotFound);

        // Kiểm tra OAuth user
        if (string.IsNullOrEmpty(user.PasswordHash))
            throw new AppException(400, "Tài khoản OAuth không thể đổi mật khẩu", ErrorCodes.OAuthUserCannotChangePassword);

        // Xác thực mật khẩu hiện tại
        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            throw new AppException(400, "Mật khẩu hiện tại không đúng", ErrorCodes.InvalidPassword);

        // Kiểm tra mật khẩu mới không trùng mật khẩu cũ
        if (BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.PasswordHash))
            throw new AppException(400, "Mật khẩu mới phải khác mật khẩu hiện tại", ErrorCodes.PasswordMustBeDifferent);

        // Lấy work factor từ configuration (mặc định 12)
        var workFactor = _configuration.GetValue<int>("BCrypt:WorkFactor", 12);

        // Hash mật khẩu mới
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, workFactor);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepo.UpdateUserAsync(user);

        // Thu hồi TẤT CẢ refresh tokens để bắt buộc đăng nhập lại
        await _refreshTokenRepo.RevokeAllByUserIdAsync(userId);

        _logger.LogInformation("User {UserId} đã đổi mật khẩu thành công. Tất cả sessions đã bị thu hồi.", userId);
    }

    /// <summary>
    /// Lấy danh sách tất cả sessions (devices) đang đăng nhập
    /// - Đánh dấu session hiện tại với IsCurrent = true
    /// </summary>
    public async Task<List<SessionDto>> GetMySessionsAsync(Guid userId, string currentTokenHash)
    {
        var sessions = await _refreshTokenRepo.GetActiveSessionsByUserIdAsync(userId);

        return sessions.Select(s => new SessionDto
        {
            Id = s.Id,
            DeviceName = s.DeviceName ?? "Unknown Device",
            IpAddress = s.IpAddress,
            Location = null, // Future enhancement
            LastUsedAt = s.LastUsedAt ?? s.CreatedAt,
            CreatedAt = s.CreatedAt,
            IsCurrent = s.TokenHash == currentTokenHash
        })
        .OrderByDescending(s => s.IsCurrent)
        .ThenByDescending(s => s.LastUsedAt)
        .ToList();
    }

    /// <summary>
    /// Đăng xuất một session cụ thể (remote logout)
    /// - KHÔNG cho phép logout session hiện tại
    /// </summary>
    public async Task LogoutSessionAsync(Guid userId, Guid sessionId, string currentTokenHash)
    {
        // Dùng GetActiveByIdAsync để chỉ lấy session còn hạn và chưa revoke
        var session = await _refreshTokenRepo.GetActiveByIdAsync(sessionId);

        if (session == null)
            throw new AppException(404, "Không tìm thấy session hoặc session đã hết hạn", ErrorCodes.SessionNotFound);

        if (session.UserId != userId)
            throw new AppException(403, "Không có quyền truy cập session này", ErrorCodes.Forbidden);

        // Không cho phép logout session hiện tại
        if (session.TokenHash == currentTokenHash)
            throw new AppException(400, "Không thể đăng xuất session hiện tại. Vui lòng sử dụng chức năng đăng xuất thông thường.", ErrorCodes.CannotLogoutCurrentSession);

        await _refreshTokenRepo.RevokeByIdAsync(sessionId);

        _logger.LogInformation("User {UserId} đã đăng xuất session {SessionId}", userId, sessionId);
    }

    /// <summary>
    /// Đăng xuất TẤT CẢ sessions NGOẠI TRỪ session hiện tại
    /// </summary>
    public async Task LogoutAllSessionsAsync(Guid userId, string currentTokenHash)
    {
        await _refreshTokenRepo.RevokeAllExceptAsync(userId, currentTokenHash);

        _logger.LogInformation("User {UserId} đã đăng xuất tất cả sessions ngoại trừ session hiện tại", userId);
    }

    /// <summary>
    /// Tính phần trăm tiến độ để lên hạng tiếp theo
    /// </summary>
    private double CalculateProgressPercentage(int currentPoints, int currentTierMin, int? nextTierMin)
    {
        if (nextTierMin == null || nextTierMin <= currentTierMin)
            return 100.0; // Đã ở hạng cao nhất

        var pointsInCurrentTier = currentPoints - currentTierMin;
        var pointsNeededForNextTier = nextTierMin.Value - currentTierMin;

        if (pointsNeededForNextTier <= 0)
            return 100.0;

        var percentage = (double)pointsInCurrentTier / pointsNeededForNextTier * 100.0;
        return Math.Min(Math.Max(percentage, 0.0), 100.0); // Clamp giữa 0-100
    }
}
