using System.Text.Json.Serialization;

namespace SmashCourt_BE.DTOs.Auth
{
    public class GoogleUserInfo
    {
        [JsonPropertyName("sub")]
        public string Sub { get; set; } = null!;

        [JsonPropertyName("email")]
        public string Email { get; set; } = null!;

        [JsonPropertyName("email_verified")]
        public bool EmailVerified { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }
}
