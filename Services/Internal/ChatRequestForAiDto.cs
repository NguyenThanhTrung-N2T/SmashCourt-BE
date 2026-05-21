namespace SmashCourt_BE.Services.Internal;

/// <summary>
/// Internal DTO for sending chat requests to AI Service.
/// Context is built by Backend server-side, never from Frontend to prevent prompt injection.
/// </summary>
internal class ChatRequestForAiDto
{
    public string Message { get; set; } = string.Empty;
    public string? Context { get; set; }  // Built by Backend, not from Frontend
    public string? SessionId { get; set; }
}
