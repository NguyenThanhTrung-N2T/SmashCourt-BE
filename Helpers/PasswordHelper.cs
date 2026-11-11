using System.Security.Cryptography;
using System.Text;

namespace SmashCourt_BE.Helpers;

/// <summary>
/// Helper để tạo password ngẫu nhiên và băm mật khẩu
/// </summary>
public static class PasswordHelper
{
    /// <summary>
    /// Hệ số bcrypt cho mọi hash mới. Đăng nhập là phần tốn CPU nhất của BE ở tải danh nghĩa nên giữ ở 11 (mức tối thiểu khuyến nghị là 10).
    /// Hệ số nằm trong chính chuỗi hash nên hash cũ với hệ số khác vẫn kiểm được bằng <c>BCrypt.Verify</c>.
    /// </summary>
    public const int BcryptWorkFactor = 11;

    /// <summary>
    /// Băm mật khẩu bằng bcrypt với <see cref="BcryptWorkFactor"/>
    /// </summary>
    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);

    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%^&*";

    /// <summary>
    /// Tạo password ngẫu nhiên với độ dài 12 ký tự
    /// Bao gồm: chữ hoa, chữ thường, số, ký tự đặc biệt
    /// </summary>
    public static string GenerateRandomPassword()
    {
        var password = new StringBuilder();
        
        // Đảm bảo có ít nhất 1 ký tự mỗi loại
        password.Append(GetRandomChar(UppercaseChars));
        password.Append(GetRandomChar(LowercaseChars));
        password.Append(GetRandomChar(DigitChars));
        password.Append(GetRandomChar(SpecialChars));

        // Thêm 8 ký tự ngẫu nhiên nữa (tổng 12 ký tự)
        var allChars = UppercaseChars + LowercaseChars + DigitChars + SpecialChars;
        for (int i = 0; i < 8; i++)
        {
            password.Append(GetRandomChar(allChars));
        }

        // Shuffle để tránh pattern cố định
        return Shuffle(password.ToString());
    }

    private static char GetRandomChar(string chars)
    {
        var randomIndex = RandomNumberGenerator.GetInt32(0, chars.Length);
        return chars[randomIndex];
    }

    private static string Shuffle(string input)
    {
        var array = input.ToCharArray();
        var n = array.Length;
        
        for (int i = n - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(0, i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
        
        return new string(array);
    }
}
