using System.Security.Cryptography;
using System.Text;

namespace SmashCourt_BE.Helpers;

/// <summary>
/// Helper để tạo password ngẫu nhiên
/// </summary>
public static class PasswordHelper
{
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
