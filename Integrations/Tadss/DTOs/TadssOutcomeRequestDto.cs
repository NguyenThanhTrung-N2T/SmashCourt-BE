namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Body của <c>POST /api/v2/decisions/{id}/outcome</c> (outcome-request.schema.json).</summary>
public class TadssOutcomeRequestDto
{
    public required TadssOutcome Outcome { get; set; }
    public required DateTimeOffset OccurredAt { get; set; }
}
