namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Response 200 của outcome (outcome-response.schema.json); cũng là phần <c>outcome</c> của chi tiết quyết định.</summary>
public class TadssOutcomeResponseDto
{
    public required Guid DecisionId { get; set; }
    public required TadssOutcome Outcome { get; set; }
    public required DateTimeOffset OccurredAt { get; set; }
    public required DateTimeOffset ReportedAt { get; set; }
}
