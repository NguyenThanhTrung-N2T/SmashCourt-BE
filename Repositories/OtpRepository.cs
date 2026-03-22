using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories;

public class OtpRepository : IOtpRepository
{
    private readonly SmashCourtContext _db;

    public OtpRepository(SmashCourtContext db)
    {
        _db = db;
    }

    // Lấy OTP mới nhất còn hiệu lực — dùng để kiểm tra cooldown 60s
    public async Task<OtpCode?> GetLatestActiveOtpAsync(Guid userId, OtpType type)
    {
        return await _db.OtpCodes
            .Where(o =>
                o.UserId == userId &&
                o.Type == type &&
                o.UsedAt == null &&
                o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
    }

    // Set used_at = now() cho tất cả OTP cũ chưa dùng — tránh OTP cũ còn hiệu lực
    public async Task InvalidateAllOtpAsync(Guid userId, OtpType type)
    {
        await _db.OtpCodes
            .Where(o => o.UserId == userId && o.Type == type && o.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.UsedAt, DateTime.UtcNow));
    }

    // tạo OTP mới 
    public async Task<OtpCode> CreateOtpAsync(OtpCode otp)
    {
        _db.OtpCodes.Add(otp);
        await _db.SaveChangesAsync();
        return otp;
    }

    // cập nhật OTP 
    public async Task UpdateOtpAsync(OtpCode otp)
    {
        _db.OtpCodes.Update(otp);
        await _db.SaveChangesAsync();
    }

    // đếm số lượng OTP còn hiệu lực — dùng để giới hạn số lần gửi OTP trong 1 khoảng thời gian
    public async Task<int> CountByUserAndTypeAsync(Guid userId, OtpType type)
    {
        return await _db.OtpCodes
            .CountAsync(o =>
                o.UserId == userId &&
                o.Type == type &&
                o.UsedAt == null &&              //Chưa dùng
                o.ExpiresAt > DateTime.UtcNow);  //Chưa hết hạn
    }
}