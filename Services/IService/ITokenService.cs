using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Services.IService
{
    public interface ITokenService
    {
        // tạo acccess token (JWT)
        string GenerateAccessToken(User user);

        // tạo refresh token 
        string GenerateRefreshToken();

        // tạo temp token dùng cho bước 2FA hoặc MustChangePassword — JWT ngắn hạn 5 phút
        // tokenType: "2fa_temp" (default) hoặc "change_password_temp"
        string GenerateTempToken(Guid userId, string tokenType = "2fa_temp");

        // Kiểm tra temp token có hợp lệ hay không, nếu hợp lệ trả về userId, ngược lại trả về null
        // expectedTokenType: nếu chỉ định, token phải có type khớp
        Guid? ValidateTempToken(string tempToken, string? expectedTokenType = null);

        // tạo token dùng cho reset password — JWT ngắn hạn 5 phút, chỉ chứa userId
        string GenerateResetPasswordToken(Guid userId);

        // Kiểm tra token reset password có hợp lệ hay không, nếu hợp lệ trả về userId, ngược lại trả về null
        Guid? ValidateResetPasswordToken(string token);
    }
}
