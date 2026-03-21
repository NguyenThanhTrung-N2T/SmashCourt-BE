using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Repositories.IRepository;

public interface IOtpRepository
{
    // lấy mã OTP mới nhất còn hiệu lực cho một người dùng và theo loại OTP
    Task<OtpCode?> GetLatestActiveOtpAsync(Guid userId, OtpType type);

    // Vô hiệu hóa tất cả OTP còn hiệu lực cho một người dùng và theo loại OTP
    Task InvalidateAllOtpAsync(Guid userId, OtpType type);

    // tạo mã OTP mới
    Task<OtpCode> CreateOtpAsync(OtpCode otp);

    // cập nhật mã OTP 
    Task UpdateOtpAsync(OtpCode otp);

    // đếm số lượng OTP còn hiệu lực cho một người dùng và theo loại OTP
    Task<int> CountByUserAndTypeAsync(Guid userId, OtpType type);
}