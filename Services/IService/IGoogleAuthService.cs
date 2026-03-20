using SmashCourt_BE.DTOs.Auth;

namespace SmashCourt_BE.Services.IService
{
    public interface IGoogleAuthService
    {
        // Tạo URL để chuyển hướng người dùng đến trang đăng nhập của Google
        string GenerateAuthUrl();

        // Xử lý callback từ Google sau khi người dùng đăng nhập thành công và nhận được mã authorization code
        Task<LoginResponse> HandleCallbackAsync(GoogleCallbackDto dto);
    }
}
