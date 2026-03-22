using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories
{
    public class OAuthAccountRepository : IOAuthAccountRepository
    {
        private readonly SmashCourtContext _db;

        public OAuthAccountRepository(SmashCourtContext db)
        {
            _db = db;
        }

        // Tạo mới OAuthAccount khi user đăng nhập bằng Google lần đầu
        public async Task<OAuthAccount> CreateOAuthAccountAsync(OAuthAccount account)
        {
            _db.OAuthAccounts.Add(account);
            await _db.SaveChangesAsync();
            return account;
        }

        // Lấy OAuthAccount theo provider user ID 
        public async Task<OAuthAccount?> GetByProviderUserIdAsync(string providerUserId)
        {
            return await _db.OAuthAccounts
                .FirstOrDefaultAsync(o =>
                    o.Provider == "GOOGLE" &&
                    o.ProviderUserId == providerUserId);
        }

        // Lấy OAuthAccount theo user ID và provider 
        public async Task<OAuthAccount?> GetByUserAndProviderAsync(Guid userId, string provider)
        {
            return await _db.OAuthAccounts
                .FirstOrDefaultAsync(o =>
                    o.UserId == userId &&
                    o.Provider == provider);
        }
    }
}
