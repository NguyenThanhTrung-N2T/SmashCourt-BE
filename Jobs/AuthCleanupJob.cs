using SmashCourt_BE.Data;
using SmashCourt_BE.Jobs.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Jobs;

public class AuthCleanupJob : IAuthCleanupJob
{
    private readonly SmashCourtContext _db;
    private readonly ILogger<AuthCleanupJob> _logger;

    public AuthCleanupJob(SmashCourtContext db, ILogger<AuthCleanupJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Chạy job cleanup hàng ngày vào lúc 2h sáng
    public async Task RunCleanupAsync()
    {
        _logger.LogInformation("Auth cleanup job started at {Time}", DateTime.UtcNow);

        await CleanupUnverifiedUsersAsync();
        await CleanupExpiredOtpAsync();
        await CleanupExpiredRefreshTokensAsync();

        _logger.LogInformation("Auth cleanup job completed at {Time}", DateTime.UtcNow);
    }

    // Xóa user chưa verify email sau 24h, chỉ xóa nếu đã có password hash (đã đăng ký hoàn chỉnh)
    public async Task CleanupUnverifiedUsersAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-24);

            var deleted = await _db.Users
                .Where(u =>
                    !u.IsEmailVerified &&
                    u.PasswordHash != null &&
                    u.CreatedAt < cutoff)
                .ExecuteDeleteAsync();

            _logger.LogInformation(
                "Cleanup unverified users: Đã xóa {Count} user", deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa user chưa verify");
        }
    }

    // Xóa OTP hết hạn sau 5 phút
    public async Task CleanupExpiredOtpAsync()
    {
        try
        {
            var deleted = await _db.OtpCodes
                .Where(o => o.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync();

            _logger.LogInformation(
                "Cleanup expired OTP: Đã xóa {Count} OTP", deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa OTP hết hạn");
        }
    }

    // Xóa refresh token hết hạn sau 7 ngày
    public async Task CleanupExpiredRefreshTokensAsync()
    {
        try
        {
            var deleted = await _db.RefreshTokens
                .Where(t => t.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync();

            _logger.LogInformation(
                "Cleanup expired refresh tokens: Đã xóa {Count} token", deleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa refresh token hết hạn");
        }
    }
}