namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>
/// Body lỗi RFC 7807 (<c>application/problem+json</c>) của TADSS. Mọi trường đều tùy chọn:
/// 415 của MVC không có <c>code</c>, và body có thể không phải problem+json (proxy, ngrok).
/// </summary>
public class TadssProblemDto
{
    public string? Code { get; set; }
    public string? Title { get; set; }
    public int? Status { get; set; }
    public string? Detail { get; set; }
    public string? TraceId { get; set; }
    public int? RetryAfterSeconds { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}
