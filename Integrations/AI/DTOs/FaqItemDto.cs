namespace SmashCourt_BE.Integrations.AI.DTOs;

/// <summary>
/// FAQ Item DTO for chatbot FAQ suggestions
/// </summary>
public class FaqItemDto
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
