using System.Security.Cryptography;
using System.Text;

namespace SmashCourt_BE.Services;

public class OtpService
{
    private readonly IConfiguration _config;

    public OtpService(IConfiguration config)
    {
        _config = config;
    }

    // Sinh 6 số ngẫu nhiên — dùng RandomNumberGenerator để tránh bias
    public string GenerateCode()
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        var number = BitConverter.ToUInt32(bytes) % 1_000_000;
        return number.ToString("D6");
    }

    // HMAC-SHA256 — key lấy từ config
    public string HashCode(string code)
    {
        var secret = _config["Otp:HmacSecret"]
            ?? throw new InvalidOperationException("Otp:HmacSecret chưa được cấu hình");

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var codeBytes = Encoding.UTF8.GetBytes(code);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(codeBytes);
        return Convert.ToHexString(hash).ToLower();
    }

    // Constant-time compare — tránh timing attack
    public bool VerifyCode(string inputCode, string storedHash)
    {
        var inputHash = HashCode(inputCode);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(inputHash),
            Encoding.UTF8.GetBytes(storedHash));
    }

    // Hash Refresh Token trước khi lưu DB — tránh lộ token nếu DB bị xâm phạm
    public string HashRefreshToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLower();
    }
}