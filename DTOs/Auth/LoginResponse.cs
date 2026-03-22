using System.Text.Json.Serialization;

namespace SmashCourt_BE.DTOs.Auth
{
    public class LoginResponse
    {
        public string Status { get; set; } = "Success"; // "Success" | "2fa_required"
        public string? AccessToken { get; set; }

        [JsonIgnore]
        public string? RefreshToken { get; set; }
        public string? TempToken { get; set; } // chỉ có khi 2fa_required

        // Thông tin user — chỉ trả khi Status = "Success"
        public UserInfo? User { get; set; }

    }

    public class UserInfo
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
        public string Role { get; set; } = null!;     // để FE redirect đúng trang
        public string Status { get; set; } = null!;
    }
}
