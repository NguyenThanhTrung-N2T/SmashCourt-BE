using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IRefreshTokenRepository
    {
        // Tạo refresh token 
        Task<RefreshToken> CreateAsync(RefreshToken token);

        // Thu hồi toàn bộ token còn hạn khi đăng nhập mới
        Task RevokeAllByUserIdAsync(Guid userId);

        // Lấy refresh token còn hạn theo token hash
        Task<RefreshToken?> GetActiveByTokenHashAsync(string tokenHash);

        // Thu hồi một token cụ thể
        Task RevokeAsync(Guid tokenId);

        // Xoay vòng refresh token: thu hồi token cũ và tạo token mới
        Task<RefreshToken> RotateRefreshTokenAsync(Guid revokeTokenId, RefreshToken newToken);

        // Thu hồi token theo token hash
        Task RevokeByTokenHashAsync(string tokenHash);

        // ===== SESSION MANAGEMENT =====

        // Lấy tất cả sessions (refresh tokens) còn hạn của user
        Task<List<RefreshToken>> GetActiveSessionsByUserIdAsync(Guid userId);

        // Lấy refresh token theo ID
        Task<RefreshToken?> GetByIdAsync(Guid tokenId);

        // Lấy refresh token còn hạn theo ID (chỉ trả về nếu chưa revoke và chưa hết hạn)
        Task<RefreshToken?> GetActiveByIdAsync(Guid tokenId);

        // Thu hồi token theo ID
        Task RevokeByIdAsync(Guid tokenId);

        // Thu hồi tất cả tokens TRỪ token hiện tại
        Task RevokeAllExceptAsync(Guid userId, string currentTokenHash);

        // Update LastUsedAt khi refresh token
        Task UpdateLastUsedAtAsync(Guid tokenId, DateTime lastUsedAt);
    }
}
