using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Profile;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Services;
using System.Security.Claims;

namespace SmashCourt_BE.Controllers;

[ApiController]
[Route("api")]
[Authorize] // Tất cả endpoints yêu cầu authentication
public class ProfileController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly OtpService _otpService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        IProfileService profileService,
        OtpService otpService,
        ILogger<ProfileController> logger)
    {
        _profileService = profileService;
        _otpService = otpService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy profile của user hiện tại
    /// </summary>
    /// <returns>Profile với thông tin khác nhau tùy theo role</returns>
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> GetMyProfile()
    {
        try
        {
            var userId = GetCurrentUserId();
            var profile = await _profileService.GetMyProfileAsync(userId);

            return Ok(ApiResponse<UserProfileDto>.Ok(profile, "Lấy thông tin profile thành công"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting profile");
            return StatusCode(500, ApiResponse<object>.Fail("Đã xảy ra lỗi khi lấy thông tin profile", ErrorCodes.InternalError));
        }
    }

    /// <summary>
    /// Cập nhật profile của user hiện tại
    /// </summary>
    /// <param name="dto">Thông tin cập nhật (fullName, phone, avatarUrl)</param>
    [HttpPut("me")]
    public async Task<ActionResult<ApiResponse<object>>> UpdateMyProfile([FromBody] UpdateProfileDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _profileService.UpdateMyProfileAsync(userId, dto);

            return Ok(ApiResponse<object>.Ok(null!, "Cập nhật profile thành công"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile");
            return StatusCode(500, ApiResponse<object>.Fail("Đã xảy ra lỗi khi cập nhật profile", ErrorCodes.InternalError));
        }
    }

    /// <summary>
    /// Đổi mật khẩu (authenticated user)
    /// Yêu cầu mật khẩu hiện tại, revoke tất cả refresh tokens sau khi đổi
    /// </summary>
    /// <param name="dto">Current password và new password</param>
    [HttpPut("me/password")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] SelfChangePasswordDto dto)
    {
        try
        {
            var userId = GetCurrentUserId();
            await _profileService.ChangePasswordAsync(userId, dto);

            return Ok(ApiResponse<object>.Ok(null!, 
                "Đổi mật khẩu thành công. " +
                "Tất cả thiết bị khác sẽ bị đăng xuất trong vòng 15 phút. " +
                "Vui lòng đăng nhập lại ngay để đảm bảo an toàn."));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password");
            return StatusCode(500, ApiResponse<object>.Fail("Đã xảy ra lỗi khi đổi mật khẩu", ErrorCodes.InternalError));
        }
    }

    /// <summary>
    /// Lấy danh sách sessions (devices) đang đăng nhập
    /// </summary>
    [HttpGet("me/sessions")]
    public async Task<ActionResult<ApiResponse<List<SessionDto>>>> GetMySessions()
    {
        try
        {
            var userId = GetCurrentUserId();
            var currentTokenHash = GetCurrentRefreshTokenHash();
            var sessions = await _profileService.GetMySessionsAsync(userId, currentTokenHash);

            return Ok(ApiResponse<List<SessionDto>>.Ok(sessions, "Lấy danh sách sessions thành công"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions");
            return StatusCode(500, ApiResponse<object>.Fail("Đã xảy ra lỗi khi lấy danh sách sessions", ErrorCodes.InternalError));
        }
    }

    /// <summary>
    /// Logout một session cụ thể (remote logout)
    /// Không cho logout session hiện tại
    /// </summary>
    /// <param name="id">Session ID</param>
    [HttpDelete("me/sessions/{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> LogoutSession(Guid id)
    {
        try
        {
            var userId = GetCurrentUserId();
            var currentTokenHash = GetCurrentRefreshTokenHash();
            await _profileService.LogoutSessionAsync(userId, id, currentTokenHash);

            return Ok(ApiResponse<object>.Ok(null!, "Logout session thành công"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging out session");
            return StatusCode(500, ApiResponse<object>.Fail("Đã xảy ra lỗi khi logout session", ErrorCodes.InternalError));
        }
    }

    /// <summary>
    /// Logout tất cả sessions TRỪ session hiện tại
    /// </summary>
    [HttpDelete("me/sessions/all")]
    public async Task<ActionResult<ApiResponse<object>>> LogoutAllSessions()
    {
        try
        {
            var userId = GetCurrentUserId();
            var currentTokenHash = GetCurrentRefreshTokenHash(); // Throw nếu không có cookie

            await _profileService.LogoutAllSessionsAsync(userId, currentTokenHash);

            return Ok(ApiResponse<object>.Ok(null!, "Logout tất cả sessions khác thành công"));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging out all sessions");
            return StatusCode(500, ApiResponse<object>.Fail("Đã xảy ra lỗi khi logout tất cả sessions", ErrorCodes.InternalError));
        }
    }

    // ===== HELPER METHODS =====

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            throw new AppException(401, "Không tìm thấy thông tin user", ErrorCodes.Unauthorized);

        return userId;
    }

    private string GetCurrentRefreshTokenHash()
    {
        // Lấy refresh token từ cookie (phải dùng "RefreshToken" với chữ R hoa - giống CookieHelper)
        if (!Request.Cookies.TryGetValue("RefreshToken", out var refreshToken) || string.IsNullOrEmpty(refreshToken))
            throw new AppException(401, "Không tìm thấy refresh token", ErrorCodes.Unauthorized);

        // Hash token bằng SHA256 để so sánh với DB (giống AuthService)
        return _otpService.HashRefreshToken(refreshToken);
    }
}
