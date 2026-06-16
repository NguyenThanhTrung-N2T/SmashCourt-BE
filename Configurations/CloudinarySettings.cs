namespace SmashCourt_BE.Configurations;

/// <summary>
/// Cấu hình Cloudinary — ánh xạ từ appsettings.json section "Cloudinary"
/// </summary>
public class CloudinarySettings
{
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey    { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
