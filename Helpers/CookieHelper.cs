using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;

namespace SmashCourt_BE.Helpers
{
    public class CookieHelper
    {
        private readonly CookieSettings _settings;

        public CookieHelper(IOptions<CookieSettings> settings)
        {
            _settings = settings.Value;
        }

        public void SetRefreshToken(HttpResponse response, string rawRefreshToken)
        {
            response.Cookies.Append("RefreshToken", rawRefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Expires = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpirationDays)
            });
        }

        public void ClearRefreshToken(HttpResponse response)
        {
            response.Cookies.Delete("RefreshToken", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None
            });
        }
    }
}
