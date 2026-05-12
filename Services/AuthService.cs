using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.DTOs.Auth;
using SmashCourt_BE.Helpers;
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
    private readonly IHttpContextAccessor _httpContextAccessor;


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
        ILoyaltyTierRepository loyaltyTierRepo,
        IHttpContextAccessor httpContextAccessor)
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
        _httpContextAccessor = httpContextAccessor;
    }
    // Gửi (hoặc gửi lại) OTP xác thực email cho user — dùng chung cho đăng ký mới và đăng ký lại
    private async Task ResendOtpForUserAsync(Guid userId, string email, string fullName)
    {
        // Cooldown check
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(userId, OtpType.EMAIL_VERIFY);
        if (latestOtp != null)
        {
            // So sánh UTC với UTC (database lưu UTC, EF Core đọc ra UTC)
            var now = DateTimeHelper.GetUtcNow(); // Trả về DateTime.UtcNow
            var secondsElapsed = (now - latestOtp.CreatedAt).TotalSeconds;
            
            // Debug log - convert sang VN time để dễ đọc
            var createdAtVN = DateTimeHelper.ToVietnamTime(latestOtp.CreatedAt);
            var nowVN = DateTimeHelper.ToVietnamTime(now);
            
            _logger.LogInformation(
                "[ResendOtpForUserAsync] OTP Cooldown Check | " +
                "CreatedAt(UTC)={CreatedAtUtc} | CreatedAt(VN)={CreatedAtVN} | " +
                "Now(UTC)={NowUtc} | Now(VN)={NowVN} | Elapsed={Elapsed}s",
                latestOtp.CreatedAt, createdAtVN,
                now, nowVN, secondsElapsed);
            
            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại OTP",
                    ErrorCodes.OtpLimitExceeded);
        }

        await _otpRepo.InvalidateAllOtpAsync(userId, OtpType.EMAIL_VERIFY);

        var rawCode = _otpService.GenerateCode();
        var otp = new OtpCode
        {
            UserId = userId,
            Type = OtpType.EMAIL_VERIFY,
            CodeHash = _otpService.HashCode(rawCode),
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        await _otpRepo.CreateOtpAsync(otp);

        try
        {
            await _emailService.SendOtpRegisterAsync(email, fullName, rawCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email");
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại", ErrorCodes.InternalError);
        }
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
                throw new AppException(409, "Email này đã được đăng ký bằng Google, vui lòng đăng nhập bằng Google", ErrorCodes.Conflict);

            // Email đã verify → tài khoản hoàn chỉnh
            if (existingUser.IsEmailVerified)
                throw new AppException(409, "Email đã được sử dụng", ErrorCodes.EmailExists);
            // VERIFY_MAIL = FALSE 
            existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 12);
            existingUser.FullName = dto.FullName.Trim();
            existingUser.Phone = dto.Phone?.Trim();
            existingUser.UpdatedAt = DateTime.UtcNow;
            await _userRepo.UpdateUserAsync(existingUser);

            // Jump straight to OTP logic using existingUser.Id
            await ResendOtpForUserAsync(existingUser.Id, existingUser.Email, existingUser.FullName);
            return;
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
            throw new AppException(400, "Email đã được sử dụng", ErrorCodes.EmailExists);
        }
        // 4. Gửi OTP
        await ResendOtpForUserAsync(user.Id, email, user.FullName);
    }

    // Xác thực OTP để hoàn tất đăng ký 
    public async Task VerifyEmailAsync(VerifyEmailDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại", ErrorCodes.NotFound);

        // 2. Đã verify rồi
        if (user.IsEmailVerified)
            throw new AppException(409, "Email đã được xác thực trước đó", ErrorCodes.BadRequest);

        // 3. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
        if (otp == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn, vui lòng yêu cầu mã mới", ErrorCodes.OtpInvalid);

        // 4. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
        {
            await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
            await _userRepo.DeleteUnverifiedAsync(user.Id);
            throw new AppException(400, "Xác thực thất bại, vui lòng đăng ký lại", ErrorCodes.OtpLimitExceeded);
        }

        // 5. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(400, $"OTP không đúng, còn {remaining} lần thử", ErrorCodes.OtpInvalid);

            // Hết 3 lần thử → khóa OTP và xóa user chưa verify để tránh tồn tại nhiều user rác
            await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.EMAIL_VERIFY);
            await _userRepo.DeleteUnverifiedAsync(user.Id);
            throw new AppException(400, "Xác thực thất bại, vui lòng đăng ký lại", ErrorCodes.OtpLimitExceeded);
        }

        // 6. OTP hợp lệ → đánh dấu đã dùng
        otp.UsedAt = DateTime.UtcNow;
        await _otpRepo.UpdateOtpAsync(otp);

        // 7. Xác thực email thành công
        user.IsEmailVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 8. Tạo hạng thành viên mặc định CHỈ cho CUSTOMER
        // STAFF/MANAGER/ADMIN không cần loyalty
        if (user.Role == UserRole.CUSTOMER)
        {
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
                try
                {
                    await _customerLoyaltyRepo.CreateAsync(customerLoyalty);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create customer loyalty record for user {UserId}", user.Id);
                    throw new AppException(500, "Đã xảy ra lỗi khi thiết lập hạng thành viên, vui lòng liên hệ hỗ trợ", ErrorCodes.InternalError);
                }
            }
        }
    }

    // Gửi lại OTP mới nếu người dùng chưa nhận được hoặc OTP cũ đã hết hạn
    public async Task ResendOtpAsync(ResendOtpDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại", ErrorCodes.NotFound);

        // 2. Kiểm tra type phù hợp với trạng thái user TRƯỚC (security fix)
        switch (dto.Type)
        {
            case OtpType.EMAIL_VERIFY:
                if (user.IsEmailVerified)
                    throw new AppException(400, "Email đã được xác thực, không cần gửi lại", ErrorCodes.BadRequest);
                break;

            case OtpType.FORGOT_PASSWORD:
                // OAuth account không có password → không cho reset
                if (user.PasswordHash == null)
                    throw new AppException(400, "Tài khoản này đăng nhập bằng Google, không có mật khẩu để đặt lại", ErrorCodes.BadRequest);
                break;

            case OtpType.TWO_FA:
                if (!user.Is2faEnabled)
                    throw new AppException(400, "Tài khoản chưa bật xác thực 2 yếu tố", ErrorCodes.BadRequest);
                break;
        }

        // 3. Kiểm tra cooldown 60s SAU khi đã validate type
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, dto.Type);
        if (latestOtp != null)
        {
            // So sánh UTC với UTC (database lưu UTC, EF Core đọc ra UTC)
            var now = DateTimeHelper.GetUtcNow(); // Trả về DateTime.UtcNow
            var secondsElapsed = (now - latestOtp.CreatedAt).TotalSeconds;
            
            // Debug log - convert sang VN time để dễ đọc
            var createdAtVN = DateTimeHelper.ToVietnamTime(latestOtp.CreatedAt);
            var nowVN = DateTimeHelper.ToVietnamTime(now);
            
            _logger.LogInformation(
                "OTP Cooldown Check | " +
                "CreatedAt(UTC)={CreatedAtUtc} | CreatedAt(VN)={CreatedAtVN} | " +
                "Now(UTC)={NowUtc} | Now(VN)={NowVN} | Elapsed={Elapsed}s",
                latestOtp.CreatedAt, createdAtVN,
                now, nowVN, secondsElapsed);
            
            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại OTP",
                    ErrorCodes.OtpLimitExceeded);
        }

        // 4. Kiểm tra giới hạn resend cho EMAIL_VERIFY
        if (dto.Type == OtpType.EMAIL_VERIFY)
        {
            // Kiểm tra tổng số lần đã gửi OTP (kể cả đã invalidate) trong 1 giờ qua
            var resendCount = await _otpRepo.CountAllByUserAndTypeAsync(
                user.Id, OtpType.EMAIL_VERIFY, TimeSpan.FromHours(1));

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

                throw new AppException(400, "Đã gửi OTP quá số lần cho phép, vui lòng đăng ký lại", ErrorCodes.OtpLimitExceeded);
            }
        }

        // 5. Invalidate OTP cũ
        await _otpRepo.InvalidateAllOtpAsync(user.Id, dto.Type);

        // 6. Tạo OTP mới
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
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại", ErrorCodes.InternalError);
        }
    }

    // Đăng nhập với email + password, trả về token nếu thành công hoặc yêu cầu 2FA nếu đã bật
    public async Task<LoginResponse> LoginAsync(LoginDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user — không báo rõ email/pass sai để tránh enumeration
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(401, "Email hoặc mật khẩu không đúng", ErrorCodes.Unauthorized);

        // 2. OAuth account
        if (user.PasswordHash == null)
            throw new AppException(400, "Tài khoản này đăng nhập bằng Google, vui lòng đăng nhập bằng Google", ErrorCodes.BadRequest);

        // 3. Email chưa verify — kiểm tra TRƯỚC mọi thứ (user chưa active)
        if (!user.IsEmailVerified)
            throw new AppException(403, "Vui lòng xác thực email trước khi đăng nhập", ErrorCodes.EmailNotVerified);

        // 4. Tài khoản bị khóa vĩnh viễn
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ", ErrorCodes.AccountLocked);

        // 5. Tài khoản bị khóa tạm do sai password
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            throw new AppException(403, $"Tài khoản tạm khóa do nhập sai mật khẩu, vui lòng thử lại sau {minutesLeft} phút", ErrorCodes.AccountLocked);
        }

        // 6. Verify password
        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            // Reset counter if last failure was outside the 15-minute window
            if (user.LastFailedLoginAt.HasValue &&
                (DateTime.UtcNow - user.LastFailedLoginAt.Value).TotalMinutes > 15)
            {
                user.FailedLoginCount = 0;
            }

            user.FailedLoginCount++;
            user.LastFailedLoginAt = DateTime.UtcNow;

            if (user.FailedLoginCount >= 5)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
                user.FailedLoginCount = 0;
                user.LastFailedLoginAt = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _userRepo.UpdateUserAsync(user);
                throw new AppException(403, "Tài khoản tạm khóa 15 phút do nhập sai mật khẩu quá 5 lần", ErrorCodes.AccountLocked);
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _userRepo.UpdateUserAsync(user);
            throw new AppException(401, "Email hoặc mật khẩu không đúng", ErrorCodes.Unauthorized);
        }

        // 7. Đăng nhập thành công → reset failed login
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastFailedLoginAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 8. Kiểm tra MustChangePassword — user phải đổi password trước khi tiếp tục
        if (user.MustChangePassword)
        {
            return new LoginResponse
            {
                Status = "must_change_password",
                TempToken = _tokenService.GenerateTempToken(user.Id, "change_password_temp"),
                Message = "Bạn cần đổi mật khẩu trước khi tiếp tục sử dụng hệ thống"
            };
        }

        // 9. Kiểm tra 2FA
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
                throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại", ErrorCodes.InternalError);
            }

            return new LoginResponse
            {
                Status = "2fa_required",
                TempToken = _tokenService.GenerateTempToken(user.Id)
            };
        }

        // 10. Không có 2FA → cấp token ngay
        // Revoke toàn bộ refresh token cũ
        await _refreshTokenRepo.RevokeAllByUserIdAsync(user.Id);

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        
        // Capture session metadata
        var (deviceName, ipAddress, userAgent) = CaptureSessionMetadata();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _otpService.HashRefreshToken(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            DeviceName = deviceName,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            LastUsedAt = DateTime.UtcNow
        };
        await _refreshTokenRepo.CreateAsync(refreshToken);

        return new LoginResponse
        {
            Status = "Success",
            AccessToken = _tokenService.GenerateAccessToken(user),
            RefreshToken = rawRefreshToken,
            User = await MapUserInfoAsync(user)
        };
    }

    // Đăng nhập với xác thực 2 yếu tố (2FA)
    public async Task<LoginResponse> Login2FAAsync(Login2FADto dto)
    {
        // 1. Validate temp token - PHẢI là 2fa_temp type
        var userId = _tokenService.ValidateTempToken(dto.TempToken, "2fa_temp");
        if (userId == null)
            throw new AppException(401, "Phiên xác thực không hợp lệ hoặc đã hết hạn, vui lòng đăng nhập lại", ErrorCodes.TokenInvalid);

        // 2. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId.Value);
        if (user == null)
            throw new AppException(401, "Tài khoản không tồn tại", ErrorCodes.Unauthorized);

        // 3. Kiểm tra email đã verify chưa — trường hợp này hiếm nhưng vẫn check để chắc chắn
        if (!user.IsEmailVerified)
            throw new AppException(403, "Vui lòng xác thực email trước khi đăng nhập", ErrorCodes.Forbidden);

        // 4. Tài khoản bị khóa do sai password
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            throw new AppException(403, $"Tài khoản tạm khóa, vui lòng thử lại sau {minutesLeft} phút", ErrorCodes.AccountLocked);
        }

        // 5. Kiểm tra status — phòng trường hợp bị khóa trong lúc đang 2FA
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ", ErrorCodes.AccountLocked);

        // 6. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.TWO_FA);
        if (otp == null)
            throw new AppException(401, "OTP không hợp lệ hoặc đã hết hạn, vui lòng đăng nhập lại", ErrorCodes.OtpInvalid);

        // 7. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
        {
            // Invalidate OTP trước khi throw (security fix)
            await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.TWO_FA);
            throw new AppException(401, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng đăng nhập lại", ErrorCodes.OtpLimitExceeded);
        }

        // 8. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(401, $"OTP không đúng, còn {remaining} lần thử", ErrorCodes.OtpInvalid);
            else
            {
                // Invalidate OTP khi hết attempts (security fix)
                await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.TWO_FA);
                throw new AppException(401, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng đăng nhập lại", ErrorCodes.OtpLimitExceeded);
            }
        }

        // 9. OTP hợp lệ → đánh dấu đã dùng và reset failed login nếu có
        otp.UsedAt = DateTime.UtcNow;
        await _otpRepo.UpdateOtpAsync(otp);

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastFailedLoginAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 10. Revoke refresh token cũ + cấp token mới
        await _refreshTokenRepo.RevokeAllByUserIdAsync(user.Id);

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        
        // Capture session metadata
        var (deviceName, ipAddress, userAgent) = CaptureSessionMetadata();
        
        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _otpService.HashRefreshToken(rawRefreshToken),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            DeviceName = deviceName,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            LastUsedAt = DateTime.UtcNow
        };
        await _refreshTokenRepo.CreateAsync(refreshToken);

        return new LoginResponse
        {
            Status = "Success",
            AccessToken = _tokenService.GenerateAccessToken(user),
            RefreshToken = rawRefreshToken,
            User = await MapUserInfoAsync(user)
        };
    }

    // Cấp lại access token mới bằng refresh token
    public async Task<(string AccessToken, string RawRefreshToken)> RefreshTokenAsync(string? rawRefreshToken)
    {
        // 1. Kiểm tra có token không
        if (string.IsNullOrEmpty(rawRefreshToken))
            throw new AppException(401, "Không tìm thấy refresh token", ErrorCodes.TokenInvalid);

        // 2. Hash → tìm trong DB
        var tokenHash = _otpService.HashRefreshToken(rawRefreshToken);
        var token = await _refreshTokenRepo.GetActiveByTokenHashAsync(tokenHash);
        if (token == null)
            throw new AppException(401, "Refresh token không hợp lệ hoặc đã hết hạn", ErrorCodes.TokenInvalid);

        // 3. Tìm user
        var user = await _userRepo.GetUserByIdAsync(token.UserId);
        if (user == null)
            throw new AppException(401, "Tài khoản không tồn tại", ErrorCodes.Unauthorized);

        // 4. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(401, "Tài khoản của bạn đã bị khóa", ErrorCodes.AccountLocked);

        // 5. Tạo refresh token mới
        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        
        // Capture session metadata (inherit from old token if not available)
        var (deviceName, ipAddress, userAgent) = CaptureSessionMetadata();
        
        var newRefreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = _otpService.HashRefreshToken(newRawRefreshToken),
            RotatedFromId = token.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            DeviceName = deviceName ?? token.DeviceName,  // Inherit from old token if not available
            IpAddress = ipAddress ?? token.IpAddress,
            UserAgent = userAgent ?? token.UserAgent,
            LastUsedAt = DateTime.UtcNow
        };

        // 6. Lưu refresh token mới và revoke token cũ
        try
        {
            await _refreshTokenRepo.RotateRefreshTokenAsync(token.Id, newRefreshToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate refresh token for user {UserId}", user.Id);
            throw new AppException(500, "Không thể làm mới phiên đăng nhập, vui lòng thử lại", ErrorCodes.InternalError);
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
    
    // ===== HELPER METHOD: Capture Session Metadata =====
    
    /// <summary>
    /// Capture session metadata từ HTTP request (UserAgent, IP Address, Device Name)
    /// </summary>
    private (string? DeviceName, string? IpAddress, string? UserAgent) CaptureSessionMetadata()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
            return (null, null, null);

        // Lấy User-Agent từ header
        var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
        
        // Truncate UserAgent nếu quá dài (max 500 chars)
        var truncatedUserAgent = UserAgentParser.TruncateUserAgent(userAgent);
        
        // Parse UserAgent thành DeviceName dễ đọc
        var deviceName = UserAgentParser.ParseToDeviceName(userAgent);
        
        // Lấy IP Address
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        return (deviceName, ipAddress, truncatedUserAgent);
    }
    
    // Map thông tin user cho client (bao gồm loyalty info nếu là customer)
    private async Task<UserInfo> MapUserInfoAsync(User user)
    {
        var userInfo = new UserInfo
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            AvatarUrl = user.AvatarUrl,
            Role = user.Role.ToString(),
            Status = user.Status.ToString()
        };

        // Chỉ load loyalty info cho CUSTOMER
        if (user.Role == UserRole.CUSTOMER)
        {
            try
            {
                var loyalty = await _customerLoyaltyRepo.GetByUserIdAsync(user.Id);
                if (loyalty?.Tier != null)
                {
                    // Lấy tất cả tiers để tính next tier
                    var allTiers = await _loyaltyTierRepo.GetAllLoyaltyTiersAsync();
                    var sortedTiers = allTiers.OrderBy(t => t.MinPoints).ToList();
                    
                    var currentTierIndex = sortedTiers.FindIndex(t => t.Id == loyalty.TierId);
                    var nextTier = currentTierIndex < sortedTiers.Count - 1 
                        ? sortedTiers[currentTierIndex + 1] 
                        : null;

                    // Tính progress percentage
                    double progressPercentage;
                    int pointsToNextTier;
                    
                    if (nextTier == null)
                    {
                        // Đã ở hạng cao nhất
                        progressPercentage = 100;
                        pointsToNextTier = 0;
                    }
                    else
                    {
                        var pointsInCurrentTier = loyalty.TotalPoints - loyalty.Tier.MinPoints;
                        var pointsNeededForNextTier = nextTier.MinPoints - loyalty.Tier.MinPoints;
                        progressPercentage = pointsNeededForNextTier > 0
                            ? Math.Round((double)pointsInCurrentTier / pointsNeededForNextTier * 100, 2)
                            : 100;
                        pointsToNextTier = nextTier.MinPoints - loyalty.TotalPoints;
                    }

                    userInfo.Loyalty = new DTOs.Loyalty.LoyaltyInfo
                    {
                        TierName = loyalty.Tier.Name,
                        TierColor = LoyaltyTierHelper.GetTierColor(loyalty.Tier.Name),
                        TierIcon = LoyaltyTierHelper.GetTierIcon(loyalty.Tier.Name),
                        DiscountRate = loyalty.Tier.DiscountRate,
                        CurrentPoints = loyalty.TotalPoints,
                        NextTierPoints = nextTier?.MinPoints ?? loyalty.TotalPoints,
                        NextTierName = nextTier?.Name,
                        ProgressPercentage = progressPercentage,
                        PointsToNextTier = pointsToNextTier
                    };
                }
            }
            catch (Exception ex)
            {
                // Log error nhưng không fail request - loyalty info là optional
                _logger.LogError(ex, "Failed to load loyalty info for user {UserId}", user.Id);
            }
        }

        return userInfo;
    }

    // Quên mật khẩu — gửi OTP về email để xác thực
    // LƯU Ý: Method này có silent return cho nhiều trường hợp để bảo mật
    public async Task ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user — silent return cho mọi trường hợp không hợp lệ
        //    Tránh lộ thông tin: email không tồn tại, chưa verify, hay là OAuth account
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null || !user.IsEmailVerified || user.PasswordHash == null)
        {
            // Silent return — không tiết lộ email có tồn tại, đã verify, hay loại tài khoản
            return;
        }

        // 2. Silent return nếu user bị LOCKED (security: không lộ thông tin account status)
        //    User bị khóa không được phép reset password
        if (user.Status == UserStatus.LOCKED)
        {
            _logger.LogWarning("Forgot password attempt for LOCKED user {UserId} ({Email})", user.Id, email);
            return; // Silent return - không báo lỗi
        }

        // 3. Kiểm tra cooldown 60s
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.FORGOT_PASSWORD);
        if (latestOtp != null)
        {
            // So sánh UTC với UTC (database lưu UTC, EF Core đọc ra UTC)
            var now = DateTimeHelper.GetUtcNow(); // Trả về DateTime.UtcNow
            var secondsElapsed = (now - latestOtp.CreatedAt).TotalSeconds;
            
            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại",
                    ErrorCodes.OtpLimitExceeded);
        }

        // 4. Invalidate OTP cũ
        await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.FORGOT_PASSWORD);

        // 5. Tạo OTP mới
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

        // 6. Gửi email
        try
        {
            await _emailService.SendOtpForgotPasswordAsync(email, user.FullName, rawCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send forgot password OTP to {Email}", email);
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại", ErrorCodes.InternalError);
        }
    }

    // Xác thực OTP để đặt lại mật khẩu
    public async Task<string> VerifyForgotPasswordOtpAsync(VerifyForgotPasswordOtpDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        // 1. Tìm user — không báo rõ để tránh enumeration
        var user = await _userRepo.GetUserByEmailAsync(email);
        if (user == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn", ErrorCodes.OtpInvalid);

        // 2. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(user.Id, OtpType.FORGOT_PASSWORD);
        if (otp == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn, vui lòng yêu cầu mã mới", ErrorCodes.OtpInvalid);

        // 3. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
            throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới", ErrorCodes.OtpLimitExceeded);

        // 4. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(400, $"OTP không đúng, còn {remaining} lần thử", ErrorCodes.OtpInvalid);
            else
            {
                // Invalidate OTP khi hết attempts (consistency fix)
                await _otpRepo.InvalidateAllOtpAsync(user.Id, OtpType.FORGOT_PASSWORD);
                throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới", ErrorCodes.OtpLimitExceeded);
            }
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
            throw new AppException(400, "Phiên đặt lại mật khẩu không hợp lệ hoặc đã hết hạn, vui lòng thực hiện lại", ErrorCodes.TokenInvalid);

        // 2. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId.Value);
        if (user == null)
            throw new AppException(400, "Tài khoản không tồn tại", ErrorCodes.BadRequest);

        // 3. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ", ErrorCodes.AccountLocked);

        // 4. Hash password mới + update
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, 12);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastFailedLoginAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 5. Revoke toàn bộ refresh token còn hạn
        // Đảm bảo tất cả session cũ bị logout sau khi đổi password
        await _refreshTokenRepo.RevokeAllByUserIdAsync(user.Id);
    }

    // Đổi mật khẩu bắt buộc sau khi admin tạo user hoặc reset password
    public async Task ChangePasswordAsync(ChangePasswordDto dto)
    {
        // 1. Validate temp token - PHẢI là change_password_temp type
        var userId = _tokenService.ValidateTempToken(dto.TempToken, "change_password_temp");
        if (userId == null)
            throw new AppException(401, "Phiên xác thực không hợp lệ hoặc đã hết hạn, vui lòng đăng nhập lại", ErrorCodes.TokenInvalid);

        // 2. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId.Value);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại", ErrorCodes.NotFound);

        // 3. Kiểm tra user có cần đổi password không
        if (!user.MustChangePassword)
            throw new AppException(400, "Tài khoản không yêu cầu đổi mật khẩu", ErrorCodes.BadRequest);

        // 4. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa, vui lòng liên hệ hỗ trợ", ErrorCodes.AccountLocked);

        // 5. Validate password mới (validation attribute đã check MinLength 8)
        // Có thể thêm validation phức tạp hơn ở đây nếu cần

        // 6. Update password và clear flag MustChangePassword
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, 12);
        user.MustChangePassword = false; // ← QUAN TRỌNG: Clear flag
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastFailedLoginAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 7. Revoke tất cả refresh tokens (force re-login với password mới)
        await _refreshTokenRepo.RevokeAllByUserIdAsync(user.Id);

        _logger.LogInformation("User {UserId} ({Email}) đã đổi mật khẩu thành công", user.Id, user.Email);
    }

    // ==================== 2FA MANAGEMENT ====================

    /// <summary>
    /// Bật 2FA - Gửi OTP xác nhận về email
    /// Áp dụng cho tất cả roles: CUSTOMER, STAFF, MANAGER, ADMIN
    /// </summary>
    public async Task Enable2FAAsync(Guid userId)
    {
        // 1. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại", ErrorCodes.NotFound);

        // 2. Kiểm tra email đã verify chưa
        if (!user.IsEmailVerified)
            throw new AppException(403, "Vui lòng xác thực email trước khi bật 2FA", ErrorCodes.Forbidden);

        // 3. Kiểm tra MustChangePassword - không cho phép thay đổi security settings khi đang bị yêu cầu đổi password
        if (user.MustChangePassword)
            throw new AppException(403, "Vui lòng đổi mật khẩu trước khi thay đổi cài đặt bảo mật", ErrorCodes.Forbidden);

        // 4. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa", ErrorCodes.AccountLocked);

        // 5. Kiểm tra đã bật 2FA chưa
        if (user.Is2faEnabled)
            throw new AppException(400, "Xác thực 2 yếu tố đã được bật trước đó", ErrorCodes.BadRequest);

        // 6. Kiểm tra cooldown 60s
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(userId, OtpType.ENABLE_2FA);
        if (latestOtp != null)
        {
            var now = DateTimeHelper.GetUtcNow();
            var secondsElapsed = (now - latestOtp.CreatedAt).TotalSeconds;

            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại OTP",
                    ErrorCodes.OtpLimitExceeded);
        }

        // 7. Invalidate OTP cũ trước khi tạo mới
        await _otpRepo.InvalidateAllOtpAsync(userId, OtpType.ENABLE_2FA);

        // 8. Tạo OTP mới
        var rawCode = _otpService.GenerateCode();
        var otp = new OtpCode
        {
            UserId = userId,
            Type = OtpType.ENABLE_2FA,
            CodeHash = _otpService.HashCode(rawCode),
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        await _otpRepo.CreateOtpAsync(otp);

        // 9. Gửi email
        try
        {
            await _emailService.SendOtp2FAAsync(user.Email, user.FullName, rawCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send ENABLE_2FA OTP to {Email}", user.Email);
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại", ErrorCodes.InternalError);
        }

        _logger.LogInformation("User {UserId} ({Email}) requested to enable 2FA", userId, user.Email);
    }

    /// <summary>
    /// Bật 2FA - Xác nhận OTP và kích hoạt
    /// </summary>
    public async Task Enable2FAVerifyAsync(Guid userId, Verify2FASettingDto dto)
    {
        // 1. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại", ErrorCodes.NotFound);

        // 2. Kiểm tra đã bật 2FA chưa
        if (user.Is2faEnabled)
            throw new AppException(400, "Xác thực 2 yếu tố đã được bật trước đó", ErrorCodes.BadRequest);

        // 3. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa", ErrorCodes.AccountLocked);

        // 4. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(userId, OtpType.ENABLE_2FA);
        if (otp == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn, vui lòng yêu cầu mã mới", ErrorCodes.OtpInvalid);

        // 5. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
        {
            await _otpRepo.InvalidateAllOtpAsync(userId, OtpType.ENABLE_2FA);
            throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới", ErrorCodes.OtpLimitExceeded);
        }

        // 6. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(400, $"OTP không đúng, còn {remaining} lần thử", ErrorCodes.OtpInvalid);

            // Hết 3 lần thử → invalidate
            await _otpRepo.InvalidateAllOtpAsync(userId, OtpType.ENABLE_2FA);
            throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới", ErrorCodes.OtpLimitExceeded);
        }

        // 7. OTP hợp lệ → đánh dấu đã dùng
        otp.UsedAt = DateTime.UtcNow;
        await _otpRepo.UpdateOtpAsync(otp);

        // 8. Bật 2FA
        user.Is2faEnabled = true;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 9. Revoke tất cả refresh tokens - force re-login với 2FA mới
        // Lý do: Security state đã thay đổi, nên invalidate tất cả sessions cũ
        await _refreshTokenRepo.RevokeAllByUserIdAsync(userId);

        _logger.LogInformation("User {UserId} ({Email}) enabled 2FA successfully", userId, user.Email);
    }

    /// <summary>
    /// Tắt 2FA - Gửi OTP xác nhận về email
    /// </summary>
    public async Task Disable2FAAsync(Guid userId)
    {
        // 1. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại", ErrorCodes.NotFound);

        // 2. Kiểm tra MustChangePassword
        if (user.MustChangePassword)
            throw new AppException(403, "Vui lòng đổi mật khẩu trước khi thay đổi cài đặt bảo mật", ErrorCodes.Forbidden);

        // 3. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa", ErrorCodes.AccountLocked);

        // 4. Kiểm tra đã bật 2FA chưa
        if (!user.Is2faEnabled)
            throw new AppException(400, "Xác thực 2 yếu tố chưa được bật", ErrorCodes.BadRequest);

        // 5. Kiểm tra cooldown 60s
        var latestOtp = await _otpRepo.GetLatestActiveOtpAsync(userId, OtpType.DISABLE_2FA);
        if (latestOtp != null)
        {
            var now = DateTimeHelper.GetUtcNow();
            var secondsElapsed = (now - latestOtp.CreatedAt).TotalSeconds;

            if (secondsElapsed < 60)
                throw new AppException(429,
                    $"Vui lòng chờ {60 - (int)secondsElapsed} giây trước khi gửi lại OTP",
                    ErrorCodes.OtpLimitExceeded);
        }

        // 6. Invalidate OTP cũ trước khi tạo mới
        await _otpRepo.InvalidateAllOtpAsync(userId, OtpType.DISABLE_2FA);

        // 7. Tạo OTP mới
        var rawCode = _otpService.GenerateCode();
        var otp = new OtpCode
        {
            UserId = userId,
            Type = OtpType.DISABLE_2FA,
            CodeHash = _otpService.HashCode(rawCode),
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow
        };
        await _otpRepo.CreateOtpAsync(otp);

        // 8. Gửi email
        try
        {
            await _emailService.SendOtp2FAAsync(user.Email, user.FullName, rawCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send DISABLE_2FA OTP to {Email}", user.Email);
            throw new AppException(500, "Không thể gửi email OTP, vui lòng thử lại", ErrorCodes.InternalError);
        }

        _logger.LogInformation("User {UserId} ({Email}) requested to disable 2FA", userId, user.Email);
    }

    /// <summary>
    /// Tắt 2FA - Xác nhận OTP và vô hiệu hóa
    /// </summary>
    public async Task Disable2FAVerifyAsync(Guid userId, Verify2FASettingDto dto)
    {
        // 1. Tìm user
        var user = await _userRepo.GetUserByIdAsync(userId);
        if (user == null)
            throw new AppException(404, "Tài khoản không tồn tại", ErrorCodes.NotFound);

        // 2. Kiểm tra đã bật 2FA chưa
        if (!user.Is2faEnabled)
            throw new AppException(400, "Xác thực 2 yếu tố chưa được bật", ErrorCodes.BadRequest);

        // 3. Kiểm tra status
        if (user.Status == UserStatus.LOCKED)
            throw new AppException(403, "Tài khoản của bạn đã bị khóa", ErrorCodes.AccountLocked);

        // 4. Tìm OTP active
        var otp = await _otpRepo.GetLatestActiveOtpAsync(userId, OtpType.DISABLE_2FA);
        if (otp == null)
            throw new AppException(400, "OTP không hợp lệ hoặc đã hết hạn, vui lòng yêu cầu mã mới", ErrorCodes.OtpInvalid);

        // 5. Kiểm tra attempt
        if (otp.AttemptCount >= 3)
        {
            await _otpRepo.InvalidateAllOtpAsync(userId, OtpType.DISABLE_2FA);
            throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới", ErrorCodes.OtpLimitExceeded);
        }

        // 6. Verify OTP
        if (!_otpService.VerifyCode(dto.OtpCode, otp.CodeHash))
        {
            otp.AttemptCount++;
            await _otpRepo.UpdateOtpAsync(otp);

            var remaining = 3 - otp.AttemptCount;
            if (remaining > 0)
                throw new AppException(400, $"OTP không đúng, còn {remaining} lần thử", ErrorCodes.OtpInvalid);

            // Hết 3 lần thử → invalidate
            await _otpRepo.InvalidateAllOtpAsync(userId, OtpType.DISABLE_2FA);
            throw new AppException(400, "OTP đã bị khóa do nhập sai quá 3 lần, vui lòng yêu cầu mã mới", ErrorCodes.OtpLimitExceeded);
        }

        // 7. OTP hợp lệ → đánh dấu đã dùng
        otp.UsedAt = DateTime.UtcNow;
        await _otpRepo.UpdateOtpAsync(otp);

        // 8. Tắt 2FA
        user.Is2faEnabled = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepo.UpdateUserAsync(user);

        // 9. Revoke tất cả refresh tokens - force re-login
        // Lý do: Nếu attacker đang giữ session, user tắt 2FA → attacker không thể tiếp tục dùng session cũ
        await _refreshTokenRepo.RevokeAllByUserIdAsync(userId);

        _logger.LogInformation("User {UserId} ({Email}) disabled 2FA successfully", userId, user.Email);
    }
}

