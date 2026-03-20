using System.Text.Json.Serialization;

namespace SmashCourt_BE.DTOs.Auth
{
    public class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = null!;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = null!;
    }
}
