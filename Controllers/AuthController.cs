using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services.IService;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;

namespace SmashCourt_BE.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly CookieHelper _cookieHelper;

    public AuthController(IAuthService authService, CookieHelper cookieHelper)
    {
        _authService = authService;
        _cookieHelper = cookieHelper;
    }

    /// <summary>
    /// Đăng ký tài khoản — gửi OTP về email để xác thực
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        await _authService.RegisterAsync(dto);
        return Ok(ApiResponse.Ok(
            message: "OTP đã được gửi đến email của bạn, có hiệu lực trong 5 phút"));
    }

    /// <summary>
    /// Xác thực OTP đăng ký tài khoản
    /// </summary>
    [HttpPost("verify-email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto)
    {
        await _authService.VerifyEmailAsync(dto);
        return Ok(ApiResponse.Ok(
            message: "Xác thực email thành công, bạn có thể đăng nhập ngay"));
    }

    /// <summary>
    /// Gửi lại OTP — dùng chung cho register, forgot-password, 2FA
    /// </summary>
    [HttpPost("resend-otp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpDto dto)
    {
        await _authService.ResendOtpAsync(dto);
        return Ok(ApiResponse.Ok(
            message: "OTP đã được gửi lại, có hiệu lực trong 5 phút"));
    }

    /// <summary>
    /// Đăng nhập bằng email + password
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);

        if (result.Status == "Success" && result.RefreshToken != null)
        {
            _cookieHelper.SetRefreshToken(Response, result.RefreshToken);
            result.RefreshToken = null;
        }

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Xác thực OTP bước 2FA — hoàn tất đăng nhập
    /// </summary>
    [HttpPost("login/2fa")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login2FA([FromBody] Login2FADto dto)
    {
        var result = await _authService.Login2FAAsync(dto);

        if (result.Status == "Success" && result.RefreshToken != null)
        {
            _cookieHelper.SetRefreshToken(Response, result.RefreshToken);
            result.RefreshToken = null;
        }

        return Ok(ApiResponse<object>.Ok(result));
    }

    /// <summary>
    /// Cấp lại Access Token bằng Refresh Token trong cookie
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh()
    {
        // Đọc refresh token từ cookie — không cần body
        var rawRefreshToken = Request.Cookies["RefreshToken"];

        var (accessToken, newRawRefreshToken) = await _authService.RefreshTokenAsync(rawRefreshToken);

        // Cập nhật refresh token mới trong cookie
        _cookieHelper.SetRefreshToken(Response, newRawRefreshToken);

        return Ok(ApiResponse<object>.Ok(new { accessToken }));
    }

    /// <summary>
    /// Đăng xuất — revoke refresh token và xóa cookie
    /// </summary>
    [HttpPost("logout")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Logout()
    {
        var rawRefreshToken = Request.Cookies["RefreshToken"];

        await _authService.LogoutAsync(rawRefreshToken);

        // Xóa cookie dù token có tồn tại hay không
        _cookieHelper.ClearRefreshToken(Response);

        return Ok(ApiResponse.Ok(message: "Đăng xuất thành công"));
    }

    /// <summary>
    /// Gửi OTP đặt lại mật khẩu về email
    /// </summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        await _authService.ForgotPasswordAsync(dto);

        // Luôn trả message giống nhau — không lộ email có tồn tại không
        return Ok(ApiResponse.Ok(
            message: "Nếu email tồn tại, OTP sẽ được gửi đến hộp thư của bạn"));
    }

    /// <summary>
    /// Xác thực OTP quên mật khẩu — trả về reset token
    /// </summary>
    [HttpPost("forgot-password/verify-otp")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> VerifyForgotPasswordOtp([FromBody] VerifyForgotPasswordOtpDto dto)
    {
        var resetToken = await _authService.VerifyForgotPasswordOtpAsync(dto);

        return Ok(ApiResponse<object>.Ok(
            data: new { resetToken },
            message: "Xác thực OTP thành công"));
    }

    /// <summary>
    /// Đặt lại mật khẩu mới sau khi xác thực OTP
    /// </summary>
    [HttpPost("forgot-password/reset")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        await _authService.ResetPasswordAsync(dto);

        return Ok(ApiResponse.Ok(
            message: "Đặt lại mật khẩu thành công, vui lòng đăng nhập lại"));
    }
}