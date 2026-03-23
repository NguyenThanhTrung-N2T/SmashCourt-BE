using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepo;
    private readonly IOtpRepository _otpRepo;
    private readonly OtpService _otpService;
    private readonly EmailService _emailService;
    private readonly ILogger<AuthService> _logger;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenRepository _refreshTokenRepo;
    private readonly JwtSettings _jwtSettings;
    private readonly ICustomerLoyaltyRepository _customerLoyaltyRepo;
    private readonly ILoyaltyTierRepository _loyaltyTierRepo;


    public AuthService(
        IUserRepository userRepo,
        IOtpRepository otpRepo,
        OtpService otpService,
        EmailService emailService,
        ILogger<AuthService> logger,
        ITokenService tokenService,
        IRefreshTokenRepository refreshTokenRepo,
        IOptions<JwtSettings> jwtSettings,
        ICustomerLoyaltyRepository customerLoyaltyRepo,
        ILoyaltyTierRepository loyaltyTierRepo)
    {
        _userRepo = userRepo;
        _otpRepo = otpRepo;
        _otpService = otpService;
        _emailService = emailService;
        _logger = logger;
        _tokenService = tokenService;
        _refreshTokenRepo = refreshTokenRepo;
        _jwtSettings = jwtSettings.Value;
        _customerLoyaltyRepo = customerLoyaltyRepo;
        _loyaltyTierRepo = loyaltyTierRepo;
    }

    // Đăng ký tài khoản mới — gửi OTP về email để xác thực
    public async Task RegisterAsync(RegisterDto dto)
    {
        // 1. Normalize email
        var email = dto.Email.Trim().ToLower();

        // 2. Kiểm tra email đã tồn tại chưa
        var existingUser = await _userRepo.GetUserByEmailAsync(email);

        if (existingUser != null)
        {
            // Email đăng ký bằng OAuth → không có password
            if (existingUser.PasswordHash == null)
                throw new AppException(400, "Email này đã được đăng ký bằng Google, vui lòng đăng nhập bằng Google");

            // Email đã verify → tài khoản hoàn chỉnh
            if (existingUser.IsEmailVerified)
                throw new AppException(400, "Email đã được sử dụng");
        }

        // 3. Tạo user mới (IsEmailVerified = false — chờ xác thực OTP)
        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password,12),
            FullName = dto.FullName.Trim(),
            Phone = dto.Phone?.Trim(),
            Role = UserRole.CUSTOMER,
            Status = UserStatus.ACTIVE,
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            user = await _userRepo.CreateUserAsync(user);
        }
        catch (DbUpdateException)
        {
            throw new AppException(400, "Email đã được sử dụng");
        }


        // 4. Kiểm tra cooldown 60s — tránh spam gửi OTP
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
        if (latestOtp != null)
        {
            var secondsElapsed = (DateTime.UtcNow - latestOtp.CreatedAt).TotalSeconds;
            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại OTP");
        }

        // 5. Xóa OTP cũ nếu có
        await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.EMAIL_VERIFY);

        // 6. Tạo OTP mới
        var rawCode = _otpService.GenerateCode();
        var codeHash = _otpService.HashCode(rawCode);

        var otp = new OtpCode
        {
            UserId = user.Id,
            Type = OtpType.EMAIL_VERIFY,
            CodeHash = codeHash,
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        await _otpRepo.CreateOtpAsync(otp);

        // 7. Gửi email OTP
        try
        {
            await _emailService.SendOtpRegisterAsync(email, user.FullName, rawCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email");
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại");
        }
    }

    // Xác thực OTP để hoàn tất đăng ký 
    public async Task VerifyEmailAsync(VerifyEmailDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại");

        // 2. Đã verify rồi
        if (user.IsEmailVerified)
            throw new AppException(400, "Email đã được xác thực trước đó");

        // 3. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
        if (otp == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn, vui lòng yêu cầu mã mới");

        // 4. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
        {
            await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
            await _userRepo.DeleteUnverifiedAsync(user.Id);
            throw new AppException(400, "Xác thực thất bại, vui lòng đăng ký lại");
        }

        // 5. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(400, $"OTP không đúng, còn {remaining} lần thử");

            // Hết 3 lần thử → khóa OTP và xóa user chưa verify để tránh tồn tại nhiều user rác
            await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
            await _userRepo.DeleteUnverifiedAsync(user.Id);
            throw new AppException(400, "Xác thực thất bại, vui lòng đăng ký lại");
        }

        // 6. OTP hợp lệ → đánh dấu đã dùng
        otp.UsedAt = DateTime.UtcNow;
        await _otpRepo.UpdateOtpAsync(otp);

        // 7. Xác thực email thành công
        user.IsEmailVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 8. Tạo hạng thành viên mặc định cho khách hàng
        var defaultTier = await _loyaltyTierRepo.GetDefaultTierAsync();
        if (defaultTier != null)
        {
            var customerLoyalty = new CustomerLoyalty
            {
                UserId = user.Id,
                TierId = defaultTier.Id,
                TotalPoints = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _customerLoyaltyRepo.CreateAsync(customerLoyalty);
        }
    }

    // Gửi lại OTP mới nếu người dùng chưa nhận được hoặc OTP cũ đã hết hạn
    public async Task ResendOtpAsync(ResendOtpDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại");

        // 2. Kiểm tra type phù hợp với trạng thái user
        switch (dto.Type)
        {
            case OtpType.EMAIL_VERIFY:
                if (user.IsEmailVerified)
                    throw new AppException(400, "Email đã được xác thực, không cần gửi lại");
                // Kiểm tra tổng số lần đã gửi OTP
                var resendCount = await _otpRepo.CountByUserAndTypeAsync(
                    user.Id, OtpType.EMAIL_VERIFY);

                if (resendCount >= 3)
                {
                    try
                    {
                        // Vô hiệu hóa OTP trước
                        await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
                        
                        // Rồi xóa user (có try-catch để xử lý nếu fail)
                        await _userRepo.DeleteUnverifiedAsync(user.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Lỗi khi xóa user chưa verify");
                    }
                    
                    throw new AppException(400, "Đã gửi OTP quá số lần cho phép, vui lòng đăng ký lại");
                }
                break;

            case OtpType.FORGOT_PASSWORD:
                // OAuth account không có password → không cho reset
                if (user.PasswordHash == null)
                    throw new AppException(400, "Tài khoản này đăng nhập bằng Google, không có mật khẩu để đặt lại");
                break;

            case OtpType.TWO_FA:
                if (!user.Is2faEnabled)
                    throw new AppException(400, "Tài khoản chưa bật xác thực 2 yếu tố");
                break;
        }

        // 3. Kiểm tra cooldown 60s
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, dto.Type);
        if (latestOtp != null)
        {
            var secondsElapsed = (DateTime.UtcNow - latestOtp.CreatedAt).TotalSeconds;
            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại OTP");
        }

        // 4. Invalidate OTP cũ
        await _otpRepo.InvalidateAllOtpAsync(user.Id, dto.Type);

        // 5. Tạo OTP mới
        var rawCode = _otpService.GenerateCode();
        var codeHash = _otpService.HashCode(rawCode);

        var otp = new OtpCode
        {
            UserId = user.Id,
            Type = dto.Type,
            CodeHash = codeHash,
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        await _otpRepo.CreateOtpAsync(otp);

        // 6. Gửi email theo type
        try
        {
            switch (dto.Type)
            {
                case OtpType.EMAIL_VERIFY:
                    await _emailService.SendOtpRegisterAsync(email, user.FullName, rawCode);
                    break;
                case OtpType.FORGOT_PASSWORD:
                    await _emailService.SendOtpForgotPasswordAsync(email, user.FullName, rawCode);
                    break;
                case OtpType.TWO_FA:
                    await _emailService.SendOtp2FAAsync(email, user.FullName, rawCode);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email for type {OtpType}", dto.Type);
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại");
        }
    }

    // Đăng nhập với email + password, trả về token nếu thành công hoặc yêu cầu 2FA nếu đã bật
    public async Task<LoginResponse> LoginAsync(LoginDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user — không báo rõ email/pass sai để tránh enumeration
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(401, "Email hoặc mật khẩu không đúng");

        // 2. OAuth account
        if (user.PasswordHash == null)
            throw new AppException(400, "Tài khoản này đăng nhập bằng Google, vui lòng đăng nhập bằng Google");

        // 3. Tài khoản bị khóa vĩnh viễn — kiểm tra TRƯỚC 2FA
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ");

        // 4. Tài khoản bị khóa tạm do sai password
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            throw new AppException(403, $"Tài khoản tạm khóa do nhập sai mật khẩu, vui lòng thử lại sau {minutesLeft} phút");
        }

        // 5. Verify password
        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;

            if (user.FailedLoginCount >= 5)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                user.FailedLoginCount = 0;
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepo.UpdateUserAsync(user);
                throw new AppException(403, "Tài khoản tạm khóa 15 phút do nhập sai mật khẩu quá 5 lần");
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _userRepo.UpdateUserAsync(user);
            throw new AppException(401, "Email hoặc mật khẩu không đúng");
        }

        // 6. Đăng nhập thành công → reset failed login
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 7. Email chưa verify
        if (!user.IsEmailVerified)
            throw new AppException(403, "Vui lòng xác thực email trước khi đăng nhập");

        // 8. Kiểm tra 2FA
        if (user.Is2faEnabled)
        {
            // Tạo OTP gửi về email
            await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.TWO_FA);

            var rawCode = _otpService.GenerateCode();
            var otp = new OtpCode
            {
                UserId = user.Id,
                Type = OtpType.TWO_FA,
                CodeHash = _otpService.HashCode(rawCode),
                AttemptCount = 0,
                ExpiresAt = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            };
            await _otpRepo.CreateOtpAsync(otp);

            try
            {
                await _emailService.SendOtp2FAAsync(email, user.FullName, rawCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send 2FA OTP to {Email}", email);
                throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại");
            }

            return new LoginResponse
            {
                Status = "2fa_required",
                TempToken = _tokenService.GenerateTempToken(user.Id)
            };
        }

        // 9. Không có 2FA → cấp token ngay
        // Revoke toàn bộ refresh token cũ
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
            RefreshToken = rawRefreshToken,
            User = MapUserInfo(user)
        };
    }

    // Đăng nhập với xác thực 2 yếu tố (2FA)
    public async Task<LoginResponse> Login2FAAsync(Login2FADto dto)
    {
        // 1. Validate temp token
        var userId = _tokenService.ValidateTempToken(dto.TempToken);
        if (userId == null)
            throw new AppException(401, "Phiên xác thực không hợp lệ hoặc đã hết hạn, vui lòng đăng nhập lại");

        // 2. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId.Value);
        if (user == null)
            throw new AppException(401, "Tài khoản không tồn tại");

        // 3. Kiểm tra email đã verify chưa — trường hợp này hiếm nhưng vẫn check để chắc chắn
        if (!user.IsEmailVerified)
            throw new AppException(403, "Vui lòng xác thực email trước khi đăng nhập");

        // 4. Tài khoản bị khóa do sai password
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            throw new AppException(403, $"Tài khoản tạm khóa, vui lòng thử lại sau {minutesLeft} phút");
        }

        // 5. Kiểm tra status — phòng trường hợp bị khóa trong lúc đang 2FA
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ");

        // 6. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.TWO_FA);
        if (otp == null)
            throw new AppException(401, "OTP không hợp lệ hoặc đã hết hạn, vui lòng đăng nhập lại");

        // 7. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
            throw new AppException(401, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng đăng nhập lại");

        // 8. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(401, $"OTP không đúng, còn {remaining} lần thử");
            else
                throw new AppException(401, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng đăng nhập lại");
        }

        // 9. OTP hợp lệ → đánh dấu đã dùng và reset failed login nếu có
        otp.UsedAt = DateTime.UtcNow;
        await _otpRepo.UpdateOtpAsync(otp);

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 10. Revoke refresh token cũ + cấp token mới
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
            RefreshToken = rawRefreshToken,
            User = MapUserInfo(user)
        };
    }

    // Cấp lại access token mới bằng refresh token
    public async Task<(string AccessToken, string RawRefreshToken)> RefreshTokenAsync(string? rawRefreshToken)
    {
        // 1. Kiểm tra có token không
        if (string.IsNullOrEmpty(rawRefreshToken))
            throw new AppException(401, "Không tìm thấy refresh token");

        // 2. Hash → tìm trong DB
        var tokenHash = _otpService.HashRefreshToken(rawRefreshToken);
        var token = await _refreshTokenRepo.GetActiveByTokenHashAsync(tokenHash);
        if (token == null)
            throw new AppException(401, "Refresh token không hợp lệ hoặc đã hết hạn");

        // 3. Tìm user
        var user = await _userRepo.GetUserByIdAsync(token.UserId);
        if (user == null)
            throw new AppException(401, "Tài khoản không tồn tại");

        // 4. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(401, "Tài khoản của bạn đã bị khóa");

        // 5. Tạo refresh token mới
        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _otpService.HashRefreshToken(newRawRefreshToken),
            RotatedFromId = token.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow
        };

        // 6. Lưu refresh token mới và revoke token cũ
        try
        {
            await _refreshTokenRepo.RotateRefreshTokenAsync(token.Id, newRefreshToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate refresh token for user {UserId}", user.Id);
            throw new AppException(500, "Không thể làm mới phiên đăng nhập, vui lòng thử lại");
        }

        // 7. Trả về access + refresh mới để controller set cookie
        return (
        _tokenService.GenerateAccessToken(user),
        newRawRefreshToken
    );
    }

    // Đăng xuất — thu hồi refresh token
    public async Task LogoutAsync(string? rawRefreshToken)
    {
        // Không có token → không cần làm gì
        if (string.IsNullOrEmpty(rawRefreshToken))
            return;

        try
        {
            var tokenHash = _otpService.HashRefreshToken(rawRefreshToken);
            await _refreshTokenRepo.RevokeByTokenHashAsync(tokenHash);
        }
        catch (Exception ex)
        {
            // Token sẽ tự hết hạn trong 7 ngày
            _logger.LogError(ex, "Failed to revoke refresh token during logout");
        }
    }
    
    // Map thông tin user cho client
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

    // Quên mật khẩu — gửi OTP về email để xác thực
    public async Task ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user — không báo lỗi nếu không tìm thấy
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
        {
            // không tìm thấy user
            return;
        }
        if(user.PasswordHash == null)
        {
            // User đăng nhập bằng OAuth
            throw new AppException(400, "Tài khoản này đăng nhập bằng Google, không có mật khẩu để đặt lại");
        }

        // 2. Kiểm tra cooldown 60s
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.FORGOT_PASSWORD);
        if (latestOtp != null)
        {
            var secondsElapsed = (DateTime.UtcNow - latestOtp.CreatedAt).TotalSeconds;
            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại");
        }

        // 3. Invalidate OTP cũ
        await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.FORGOT_PASSWORD);

        // 4. Tạo OTP mới
        var rawCode = _otpService.GenerateCode();
        var otp = new OtpCode
        {
            UserId = user.Id,
            Type = OtpType.FORGOT_PASSWORD,
            CodeHash = _otpService.HashCode(rawCode),
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        await _otpRepo.CreateOtpAsync(otp);

        // 5. Gửi email
        try
        {
            await _emailService.SendOtpForgotPasswordAsync(email, user.FullName, rawCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send forgot password OTP to {Email}", email);
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại");
        }
    }

    // Xác thực OTP để đặt lại mật khẩu
    public async Task<string> VerifyForgotPasswordOtpAsync(VerifyForgotPasswordOtpDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user — không báo rõ để tránh enumeration
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn");

        // 2. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.FORGOT_PASSWORD);
        if (otp == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn, vui lòng yêu cầu mã mới");

        // 3. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
            throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới");

        // 4. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(400, $"OTP không đúng, còn {remaining} lần thử");
            else
                throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới");
        }

        // 5. OTP hợp lệ → đánh dấu đã dùng
        otp.UsedAt = DateTime.UtcNow;
        await _otpRepo.UpdateOtpAsync(otp);

        // 6. Tạo temp token để dùng ở bước reset password
        // Dùng lại GenerateTempToken với claim type riêng
        return _tokenService.GenerateResetPasswordToken(user.Id);
    }

    // Đặt lại mật khẩu mới sau khi xác thực OTP thành công
    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        // 1. Validate reset token
        var userId = _tokenService.ValidateResetPasswordToken(dto.ResetToken);
        if (userId == null)
            throw new AppException(400, "Phiên đặt lại mật khẩu không hợp lệ hoặc đã hết hạn, vui lòng thực hiện lại");

        // 2. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId.Value);
        if (user == null)
            throw new AppException(400, "Tài khoản không tồn tại");

        // 3. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ");

        // 4. Hash password mới + update
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 5. Revoke toàn bộ refresh token còn hạn
        // Đảm bảo tất cả session cũ bị logout sau khi đổi password
        await _refreshTokenRepo.RevokeAllByUserIdAsync(user.Id);
    }


}