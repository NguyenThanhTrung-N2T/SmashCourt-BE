using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Controllers;

[ApiController]
[Route("api/auth/google")]
public class GoogleAuthController : ControllerBase
{
    private readonly IGoogleAuthService _googleAuthService;
    private readonly CookieHelper _cookieHelper;

    public GoogleAuthController(
        IGoogleAuthService googleAuthService,
        CookieHelper cookieHelper)
    {
        _googleAuthService = googleAuthService;
        _cookieHelper = cookieHelper;
    }

    /// <summary>
    /// Lấy Google OAuth URL để FE redirect
    /// </summary>
    [HttpGet("url")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAuthUrl()
    {
        var url = _googleAuthService.GenerateAuthUrl();
        return Ok(ApiResponse<object>.Ok(new { url }));
    }

    /// <summary>
    /// Xử lý callback từ Google sau khi user đăng nhập
    /// </summary>
    [HttpPost("callback")]
    [EnableRateLimiting("sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Callback([FromBody] GoogleCallbackDto dto)
    {
        var result = await _googleAuthService.HandleCallbackAsync(dto);

        if (result.Status == "Success" && result.RefreshToken != null)
        {
            _cookieHelper.SetRefreshToken(Response, result.RefreshToken);
            result.RefreshToken = null;
        }

        return Ok(ApiResponse<object>.Ok(result));
    }
}
