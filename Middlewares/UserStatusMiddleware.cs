using System.Security.Claims;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Middlewares;

/// <summary>
/// Middleware kiểm tra status của user trên mỗi request
/// Chặn ngay lập tức nếu user bị LOCKED hoặc INACTIVE
/// 
/// QUAN TRỌNG: Middleware này giải quyết security hole:
/// - User bị lock → tokens bị revoke
/// - Nhưng access token cũ vẫn valid cho đến khi expire
/// - Middleware này chặn ngay lập tức, không cần đợi token expire
/// </summary>
public class UserStatusMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserStatusMiddleware> _logger;

    public UserStatusMiddleware(RequestDelegate next, ILogger<UserStatusMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepo)
    {
        // Chỉ check cho authenticated requests
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                try
                {
                    var user = await userRepo.GetUserByIdAsync(userId);
                    
                    // Check status
                    if (user == null)
                    {
                        _logger.LogWarning("User {UserId} not found in database but has valid token", userId);
                        
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                            "Tài khoản không tồn tại",
                            ErrorCodes.UserNotFound));
                        
                        return;
                    }
                    
                    if (user.Status == UserStatus.LOCKED)
                    {
                        _logger.LogWarning("Blocked request from LOCKED user {UserId}", userId);
                        
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                            "Tài khoản đã bị khóa, vui lòng liên hệ hỗ trợ",
                            ErrorCodes.AccountLocked));
                        
                        return;
                    }
                    
                    if (user.Status == UserStatus.INACTIVE)
                    {
                        _logger.LogWarning("Blocked request from INACTIVE user {UserId}", userId);
                        
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        
                        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
                            "Tài khoản đã bị vô hiệu hóa",
                            ErrorCodes.AccountLocked));
                        
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // Log error nhưng không block request
                    // Nếu DB down, vẫn cho request đi qua (fail-open)
                    _logger.LogError(ex, "Error checking user status for {UserId}", userId);
                }
            }
        }
        
        await _next(context);
    }
}

