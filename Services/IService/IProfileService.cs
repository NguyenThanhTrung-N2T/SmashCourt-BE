using SmashCourt_BE.DTOs.Profile;

namespace SmashCourt_BE.Services.IService;

/// <summary>
/// Service quản lý profile và session của user hiện tại
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Lấy thông tin profile của user hiện tại
    /// - CUSTOMER: bao gồm thông tin Loyalty
    /// - STAFF/MANAGER: bao gồm thông tin Branch
    /// </summary>
    /// <param name="userId">ID của user hiện tại</param>
    /// <returns>Thông tin profile đầy đủ</returns>
    Task<UserProfileDto> GetMyProfileAsync(Guid userId);

    /// <summary>
    /// Cập nhật thông tin profile của user hiện tại
    /// Chỉ cho phép cập nhật: fullName, phone, avatarUrl
    /// </summary>
    /// <param name="userId">ID của user hiện tại</param>
    /// <param name="dto">Thông tin cần cập nhật</param>
    Task UpdateMyProfileAsync(Guid userId, UpdateProfileDto dto);

    /// <summary>
    /// Đổi mật khẩu cho user hiện tại
    /// - Xác thực mật khẩu hiện tại
    /// - Validate mật khẩu mới
    /// - Thu hồi TẤT CẢ refresh tokens sau khi đổi
    /// - OAuth users không thể đổi mật khẩu
    /// </summary>
    /// <param name="userId">ID của user hiện tại</param>
    /// <param name="dto">Thông tin đổi mật khẩu</param>
    Task ChangePasswordAsync(Guid userId, SelfChangePasswordDto dto);

    /// <summary>
    /// Lấy danh sách tất cả sessions (devices) đang đăng nhập
    /// - Đánh dấu session hiện tại với IsCurrent = true
    /// </summary>
    /// <param name="userId">ID của user hiện tại</param>
    /// <param name="currentTokenHash">Hash của refresh token hiện tại</param>
    /// <returns>Danh sách sessions</returns>
    Task<List<SessionDto>> GetMySessionsAsync(Guid userId, string currentTokenHash);

    /// <summary>
    /// Đăng xuất một session cụ thể (remote logout)
    /// - KHÔNG cho phép logout session hiện tại
    /// </summary>
    /// <param name="userId">ID của user hiện tại</param>
    /// <param name="sessionId">ID của session cần logout</param>
    /// <param name="currentTokenHash">Hash của refresh token hiện tại</param>
    Task LogoutSessionAsync(Guid userId, Guid sessionId, string currentTokenHash);

    /// <summary>
    /// Đăng xuất TẤT CẢ sessions NGOẠI TRỪ session hiện tại
    /// </summary>
    /// <param name="userId">ID của user hiện tại</param>
    /// <param name="currentTokenHash">Hash của refresh token hiện tại</param>
    Task LogoutAllSessionsAsync(Guid userId, string currentTokenHash);
}
