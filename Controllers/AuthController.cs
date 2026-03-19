using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services.IService;
using Microsoft.Extensions.Options;

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
        // kiểm tra dữ liệu đầu vào 
        if (!ModelState.IsValid)
        {
            return BadRequest("Thông tin đăng ký không hợp lệ");
        }
        await _authService.RegisterAsync(dto);
        return Ok(new { message = "OTP đã được gửi đến email của bạn, có hiệu lực trong 5 phút" });
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
        if (!ModelState.IsValid)
            return BadRequest("Thông tin không hợp lệ");

        await _authService.VerifyEmailAsync(dto);
        return Ok(new { message = "Xác thực email thành công, bạn có thể đăng nhập ngay" });
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
        if (!ModelState.IsValid)
            return BadRequest("Thông tin không hợp lệ");

        await _authService.ResendOtpAsync(dto);
        return Ok(new { message = "OTP đã được gửi lại, có hiệu lực trong 5 phút" });
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
        if (!ModelState.IsValid)
            return BadRequest("Thông tin không hợp lệ");

        var result = await _authService.LoginAsync(dto);

        if (result.Status == "Success" && result.RefreshToken != null)
        {
            _cookieHelper.SetRefreshToken(Response, result.RefreshToken);
            result.RefreshToken = null;
        }

        return Ok(result);
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
        if (!ModelState.IsValid)
            return BadRequest("Thông tin không hợp lệ");

        var result = await _authService.Login2FAAsync(dto);

        if (result.Status == "Success" && result.RefreshToken != null)
        {
            _cookieHelper.SetRefreshToken(Response, result.RefreshToken);
            result.RefreshToken = null;
        }

        return Ok(result);
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

        return Ok(new { accessToken });
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

        return Ok(new { message = "Đăng xuất thành công" });
    }


}