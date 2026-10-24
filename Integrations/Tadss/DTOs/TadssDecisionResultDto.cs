namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Response 200 của <c>POST /api/v2/decisions</c> (decision-result.schema.json).</summary>
public class TadssDecisionResultDto
{
    public required Guid DecisionId { get; set; }
    public required Guid ReservationId { get; set; }
    public required TadssDecisionStatus Status { get; set; }
    /// <summary>Có giá trị khi và chỉ khi <c>Status = APPROVED</c>.</summary>
    public TadssDepositDto? Deposit { get; set; }
    public required string StrategyName { get; set; }
    public required string StrategyVersion { get; set; }
    public required string ExecutionType { get; set; }
    public required bool FallbackUsed { get; set; }
    /// <summary>Giữ dạng chuỗi: TADSS được thêm mã mới mà không đổi phiên bản, mã lạ không được làm lỗi.</summary>
    public required string ReasonCode { get; set; }
    public required List<string> Reasons { get; set; }
    public required List<TadssRecommendationDto> Recommendations { get; set; }
    /// <summary>Có giá trị khi và chỉ khi <c>Status = NEED_APPROVAL</c>.</summary>
    public DateTimeOffset? ApprovalExpiresAt { get; set; }
    public required DateTimeOffset EvaluatedAt { get; set; }
}
