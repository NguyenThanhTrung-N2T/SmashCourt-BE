using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Services.IService
{
    public interface ITokenService
    {
        // tạo acccess token (JWT)
        string GenerateAccessToken(User user);

        // tạo refresh token 
        string GenerateRefreshToken();

        // tạo temp token dùng cho bước 2FA — JWT ngắn hạn 5 phút, chỉ chứa userId
        string GenerateTempToken(Guid userId); // dùng cho 2FA

        // Kiểm tra temp token có hợp lệ hay không, nếu hợp lệ trả về userId, ngược lại trả về null
        Guid? ValidateTempToken(string tempToken);
    }
}
