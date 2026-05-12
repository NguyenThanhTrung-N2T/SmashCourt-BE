namespace SmashCourt_BE.DTOs.Profile;

/// <summary>
/// DTO cho GET /api/me/sessions - Danh sách sessions (devices) đang đăng nhập
/// </summary>
public class SessionDto
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = null!;  // "Chrome on Windows", "Safari on iPhone"
    public string? IpAddress { get; set; }
    public string? Location { get; set; }            // Optional - "Hanoi, Vietnam" (future)
    public DateTime LastUsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCurrent { get; set; }              // True nếu là session hiện tại
}
