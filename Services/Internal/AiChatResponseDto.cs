namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO representing raw chat response from AI Service.
/// This is validated and transformed into ChatResponseDto by AIResponseFormatterService.
/// </summary>
public class AiChatResponseDto
{
    public string Reply { get; set; } = string.Empty;
    public List<string> Suggestions { get; set; } = new();
    public string Model { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public DateTime GeneratedAt { get; set; }
}
