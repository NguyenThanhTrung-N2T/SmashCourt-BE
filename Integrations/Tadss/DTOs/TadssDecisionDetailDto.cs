namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>
/// Response 200 của <c>GET /api/v2/decisions/{id}</c> (decision-detail.schema.json).
/// Bản ghi bất biến: sau override, <c>Result.Status</c> vẫn là NEED_APPROVAL và <c>Override</c> mang kết quả duyệt.
/// </summary>
public class TadssDecisionDetailDto
{
    public required Guid DecisionId { get; set; }
    public required Guid ReservationId { get; set; }
    public required TadssDecisionRequestDto Request { get; set; }
    public required TadssDecisionDetailResultDto Result { get; set; }
    public required TadssDecisionExecutionDto Execution { get; set; }
    public TadssOverrideResponseDto? Override { get; set; }
    public TadssOutcomeResponseDto? Outcome { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string? CorrelationId { get; set; }
}
