using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly SmashCourtContext _db;

        public RefreshTokenRepository(SmashCourtContext db)
        {
            _db = db;
        }

        // Lưu refresh token mới vào DB
        public async Task<RefreshToken> CreateAsync(RefreshToken token)
        {
            _db.RefreshTokens.Add(token);
            await _db.SaveChangesAsync();
            return token;
        }

        // Revoke toàn bộ token còn hạn khi đăng nhập mới
        public async Task RevokeAllByUserIdAsync(Guid userId)
        {
            var now = DateTimeHelper.GetNowInVietnam();
            await _db.RefreshTokens
                .Where(t =>
                    t.UserId == userId &&
                    t.RevokedAt == null &&
                    t.ExpiresAt > now)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.RevokedAt, now));
        }

        // Lấy refresh token còn hạn theo token hash
        public async Task<RefreshToken?> GetActiveByTokenHashAsync(string tokenHash)
        {
            var now = DateTimeHelper.GetNowInVietnam();
            return await _db.RefreshTokens
                .FirstOrDefaultAsync(t =>
                    t.TokenHash == tokenHash &&
                    t.RevokedAt == null &&
                    t.ExpiresAt > now);
        }

        // Revoke một token cụ thể
        public async Task RevokeAsync(Guid tokenId)
        {
            await _db.RefreshTokens
                .Where(t => t.Id == tokenId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        }

        // Xoay vòng refresh token: revoke token cũ và tạo token mới
        public async Task<RefreshToken> RotateRefreshTokenAsync(Guid revokeTokenId, RefreshToken newToken)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                await _db.RefreshTokens
                    .Where(t => t.Id == revokeTokenId)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));

                _db.RefreshTokens.Add(newToken);
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return newToken;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw; // Rethrow bất kỳ exception nào lên Service xử lý
            }
        }

        // Thu hồi token theo token hash
        public async Task RevokeByTokenHashAsync(string tokenHash)
        {
            await _db.RefreshTokens
                .Where(t =>
                    t.TokenHash == tokenHash &&
                    t.RevokedAt == null)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        }

    }
}
