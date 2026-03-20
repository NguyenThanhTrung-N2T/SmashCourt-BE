using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IOAuthAccountRepository
    {
        // Tạo mới một OAuthAccount
        Task<OAuthAccount> CreateOAuthAccountAsync(OAuthAccount account);

        // Lấy OAuthAccount theo provider user ID
        Task<OAuthAccount?> GetByProviderUserIdAsync(string providerUserId);

        // Lấy OAuthAccount theo user ID và provider
        Task<OAuthAccount?> GetByUserAndProviderAsync(Guid userId, string provider);
    }
}
