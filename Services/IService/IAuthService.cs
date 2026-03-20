using SmashCourt_BE.DTOs.Auth;

namespace SmashCourt_BE.Services.IService;

public interface IAuthService
{
    // đăng ký tài khoản mới
    Task RegisterAsync(RegisterDto dto);

    // Xác thực OTP để kích hoạt tài khoản
    Task VerifyEmailAsync(VerifyEmailDto dto);

    // Gửi lại OTP mới nếu người dùng chưa nhận được hoặc OTP cũ đã hết hạn
    Task ResendOtpAsync(ResendOtpDto dto);

    // Đăng nhập và nhận token JWT
    Task<LoginResponse> LoginAsync(LoginDto dto);

    // Đăng nhập với xác thực 2 yếu tố (2FA)
    Task<LoginResponse> Login2FAAsync(Login2FADto dto);

    // Cấp lại access token mới bằng refresh token
    Task<(string AccessToken, string RawRefreshToken)> RefreshTokenAsync(string? rawRefreshToken);

    // Đăng xuất và thu hồi refresh token
    Task LogoutAsync(string? rawRefreshToken);

    // Quên mật khẩu
    Task ForgotPasswordAsync(ForgotPasswordDto dto);

    // Xác thực OTP để đặt lại mật khẩu
    Task<string> VerifyForgotPasswordOtpAsync(VerifyForgotPasswordOtpDto dto);

    // Đặt lại mật khẩu mới sau khi xác thực OTP thành công
    Task ResetPasswordAsync(ResetPasswordDto dto);
}