namespace SmashCourt_BE.DTOs.Auth
{
    public class GoogleSettings
    {
        public string ClientId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
        public string RedirectUri { get; set; } = null!;
    }
}
