namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// Chat Response DTO from AI Service
/// </summary>
public class ChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public DateTime GeneratedAt { get; set; }
}
