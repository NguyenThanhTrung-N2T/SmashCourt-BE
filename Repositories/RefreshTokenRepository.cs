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
            // So sánh UTC với UTC
            var now = DateTimeHelper.GetUtcNow(); // Trả về DateTime.UtcNow
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
            // So sánh UTC với UTC
            var now = DateTimeHelper.GetUtcNow(); // Trả về DateTime.UtcNow
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

        // ===== SESSION MANAGEMENT =====

        // Lấy tất cả sessions (refresh tokens) còn hạn của user
        public async Task<List<RefreshToken>> GetActiveSessionsByUserIdAsync(Guid userId)
        {
            var now = DateTimeHelper.GetUtcNow();
            return await _db.RefreshTokens
                .AsNoTracking()
                .Where(t =>
                    t.UserId == userId &&
                    t.RevokedAt == null &&
                    t.ExpiresAt > now)
                .OrderByDescending(t => t.LastUsedAt ?? t.CreatedAt)
                .ToListAsync();
        }

        // Lấy refresh token theo ID
        public async Task<RefreshToken?> GetByIdAsync(Guid tokenId)
        {
            return await _db.RefreshTokens.FindAsync(tokenId);
        }

        // Lấy refresh token còn hạn theo ID (chỉ trả về nếu chưa revoke và chưa hết hạn)
        public async Task<RefreshToken?> GetActiveByIdAsync(Guid tokenId)
        {
            var now = DateTimeHelper.GetUtcNow();
            return await _db.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t =>
                    t.Id == tokenId &&
                    t.RevokedAt == null &&
                    t.ExpiresAt > now);
        }

        // Thu hồi token theo ID
        public async Task RevokeByIdAsync(Guid tokenId)
        {
            await _db.RefreshTokens
                .Where(t => t.Id == tokenId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        }

        // Thu hồi tất cả tokens TRỪ token hiện tại
        public async Task RevokeAllExceptAsync(Guid userId, string currentTokenHash)
        {
            var now = DateTimeHelper.GetUtcNow();
            await _db.RefreshTokens
                .Where(t =>
                    t.UserId == userId &&
                    t.TokenHash != currentTokenHash &&
                    t.RevokedAt == null &&
                    t.ExpiresAt > now)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.RevokedAt, now));
        }

        // Update LastUsedAt khi refresh token
        public async Task UpdateLastUsedAtAsync(Guid tokenId, DateTime lastUsedAt)
        {
            await _db.RefreshTokens
                .Where(t => t.Id == tokenId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(t => t.LastUsedAt, lastUsedAt));
        }

    }
}
