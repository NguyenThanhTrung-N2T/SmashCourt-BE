namespace SmashCourt_BE.Jobs.Interfaces;

public interface IAuthCleanupJob
{
    // Chạy job cleanup - xóa user chưa verify, OTP hết hạn, refresh token hết hạn
    Task RunCleanupAsync();

    // Job 1 — Xóa user chưa verify sau 24h
    Task CleanupUnverifiedUsersAsync();

    // Job 2 — Xóa OTP hết hạn
    Task CleanupExpiredOtpAsync();

    // Job 3 — Xóa refresh token hết hạn
    Task CleanupExpiredRefreshTokensAsync();
}