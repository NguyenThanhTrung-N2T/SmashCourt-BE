using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using System.Text.Json.Serialization;

namespace SmashCourt_BE.Services
{
    public class GoogleAuthService : IGoogleAuthService
    {
        private readonly GoogleSettings _googleSettings;
        private readonly IMemoryCache _cache;
        private readonly IUserRepository _userRepo;
        private readonly IOAuthAccountRepository _oauthRepo;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly ITokenService _tokenService;
        private readonly OtpService _otpService;
        private readonly HttpClient _httpClient;
        private readonly ILogger<GoogleAuthService> _logger;

        public GoogleAuthService(
            IOptions<GoogleSettings> googleSettings,
            IMemoryCache cache,
            IUserRepository userRepo,
            IOAuthAccountRepository oauthRepo,
            IRefreshTokenRepository refreshTokenRepo,
            ITokenService tokenService,
            OtpService otpService,
            IHttpClientFactory httpClientFactory,
            ILogger<GoogleAuthService> logger)
        {
            _googleSettings = googleSettings.Value;
            _cache = cache;
            _userRepo = userRepo;
            _oauthRepo = oauthRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _tokenService = tokenService;
            _otpService = otpService;
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;

            if (string.IsNullOrEmpty(_googleSettings.ClientId) ||
        string.IsNullOrEmpty(_googleSettings.ClientSecret) ||
        string.IsNullOrEmpty(_googleSettings.RedirectUri))
                throw new InvalidOperationException("Google OAuth chưa được cấu hình đầy đủ");
        }

        // Tạo URL để chuyển hướng người dùng đến trang đăng nhập của Google
        public string GenerateAuthUrl()
        {
            // 1. Tạo state ngẫu nhiên
            var state = Guid.NewGuid().ToString("N");

            // 2. Lưu state vào cache 5 phút
            _cache.Set($"oauth_state:{state}", true, TimeSpan.FromMinutes(5));

            // 3. Build Google OAuth URL
            var queryParams = new Dictionary<string, string>
            {
                ["client_id"] = _googleSettings.ClientId,
                ["redirect_uri"] = _googleSettings.RedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["access_type"] = "offline"
            };

            var queryString = string.Join("&",
                queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

            return $"https://accounts.google.com/o/oauth2/v2/auth?{queryString}";
        }

        // Xử lý callback từ Google sau khi người dùng đăng nhập thành công

        public async Task<LoginResponse> HandleCallbackAsync(GoogleCallbackDto dto)
        {
            // 1. Verify state
            var stateKey = $"oauth_state:{dto.State}";
            if (!_cache.TryGetValue(stateKey, out _))
                throw new AppException(400, "Phiên xác thực không hợp lệ hoặc đã hết hạn");

            // 2. Xóa state — chỉ dùng 1 lần
            _cache.Remove(stateKey);

            // 3. Đổi code lấy access_token từ Google
            var tokenResponse = await ExchangeCodeForTokenAsync(dto.Code);
            if (tokenResponse == null)
                throw new AppException(400, "Không thể xác thực với Google, vui lòng thử lại");

            // 4. Lấy thông tin user từ Google
            var googleUser = await GetGoogleUserInfoAsync(tokenResponse.AccessToken);
            if (googleUser == null)
                throw new AppException(400, "Không thể lấy thông tin từ Google, vui lòng thử lại");

            // 5. Kiểm tra email đã verify chưa
            if (!googleUser.EmailVerified)
                throw new AppException(400, "Email chưa được xác thực với Google");

            var email = googleUser.Email.Trim().ToLower();

            // 6. Xử lý các nhánh
            var user = await _userRepo.GetUserByEmailAsync(email);

            if (user == null)
            {
                // Nhánh 1 — Email chưa tồn tại → tạo user mới
                user = new User
                {
                    Email = email,
                    FullName = googleUser.Name,
                    AvatarUrl = googleUser.Picture,
                    Role = UserRole.CUSTOMER,
                    Status = UserStatus.ACTIVE,
                    IsEmailVerified = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                user = await _userRepo.CreateUserAsync(user);

                // Lưu OAuth account
                var oauthAccount = new OAuthAccount
                {
                    UserId = user.Id,
                    Provider = "GOOGLE",
                    ProviderUserId = googleUser.Sub,
                    CreatedAt = DateTime.UtcNow
                };
                try
                {
                    await _oauthRepo.CreateOAuthAccountAsync(oauthAccount);
                }
                catch (DbUpdateException)
                {
                    throw new AppException(400, "Google account này đã được liên kết với tài khoản khác");
                }
            }
            else if (user.PasswordHash != null)
            {
                // Nhánh 4 — Email/Password account → không cho login bằng Google
                throw new AppException(400,
                    "Email này đã đăng ký bằng mật khẩu, vui lòng đăng nhập bằng email");
            }
            else if (user.Status == UserStatus.LOCKED)
            {
                // Nhánh 3 — Tài khoản bị khóa
                throw new AppException(403,
                    "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ");
            }
            else
            {
                // Nhánh 2 — Email tồn tại, là OAuth account (PasswordHash = null)
                var existingOAuth = await _oauthRepo.GetByProviderUserIdAsync(googleUser.Sub);

                if (existingOAuth == null)
                {
                    // Chưa liên kết Google account → tự động liên kết
                    var oauthAccount = new OAuthAccount
                    {
                        UserId = user.Id,
                        Provider = "GOOGLE",
                        ProviderUserId = googleUser.Sub,
                        CreatedAt = DateTime.UtcNow
                    };
                    try
                    {
                        await _oauthRepo.CreateOAuthAccountAsync(oauthAccount);
                    }
                    catch (DbUpdateException)
                    {
                        throw new AppException(400, "Google account này đã được liên kết với tài khoản khác");
                    }
                }
                else if (existingOAuth.UserId != user.Id)
                {
                    // Google Sub này đã liên kết với user khác → account takeover
                    throw new AppException(400, "Google account này đã được liên kết với tài khoản khác");
                }
                // existingOAuth.UserId == user.Id → đã liên kết đúng → đăng nhập bình thường
            }

            // 7. Revoke refresh token cũ + cấp token mới
            await _refreshTokenRepo.RevokeAllByUserIdAsync(user.Id);

            var rawRefreshToken = _tokenService.GenerateRefreshToken();
            var refreshToken = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = _otpService.HashRefreshToken(rawRefreshToken),
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow
            };
            await _refreshTokenRepo.CreateAsync(refreshToken);

            return new LoginResponse
            {
                Status = "Success",
                AccessToken = _tokenService.GenerateAccessToken(user),
                RefreshToken = rawRefreshToken, // Controller set cookie, không trả body
                User = MapUserInfo(user)
            };
        }

        // Đổi authorization code lấy access_token
        private async Task<GoogleTokenResponse?> ExchangeCodeForTokenAsync(string code)
        {
            var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _googleSettings.ClientId,
                ["client_secret"] = _googleSettings.ClientSecret,
                ["redirect_uri"] = _googleSettings.RedirectUri,
                ["grant_type"] = "authorization_code"
            });

            try
            {
                var response = await _httpClient.PostAsync(
                    "https://oauth2.googleapis.com/token", requestBody);

                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>();
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to exchange Google OAuth code for token");
                return null;
            }
        }

        // Lấy thông tin user từ Google
        private async Task<GoogleUserInfo?> GetGoogleUserInfoAsync(string accessToken)
        {
            try
            {
                // Dùng HttpRequestMessage thay vì DefaultRequestHeaders
                // Mỗi request có header riêng — không bị shared state
                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    "https://www.googleapis.com/oauth2/v3/userinfo");

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode) return null;

                return await response.Content.ReadFromJsonAsync<GoogleUserInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get Google user info");
                return null;
            }
        }

        // Map User entity sang UserInfo DTO để trả về FE
        private static UserInfo MapUserInfo(User user) => new()
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            AvatarUrl = user.AvatarUrl,
            Role = user.Role.ToString(),
            Status = user.Status.ToString()
        };
    }

    
}
