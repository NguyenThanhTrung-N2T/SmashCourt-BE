namespace SmashCourt_BE.Services.AI.DTOs;

/// <summary>
/// DTOs for FastAPI Chat endpoints
/// </summary>

internal sealed class FastApiFaqListResponse
{
    public List<FastApiFaqItem> Items { get; set; } = [];
    public int Total { get; set; }
}

internal sealed class FastApiFaqItem
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Answer { get; set; }
}
