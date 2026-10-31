namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Phần <c>result</c> của chi tiết quyết định. <c>StrategyStatus</c> là kết luận gốc của strategy, <c>Status</c> là kết quả cuối sau runtime.</summary>
public class TadssDecisionDetailResultDto
{
    public required TadssDecisionStatus Status { get; set; }
    public required TadssDecisionStatus StrategyStatus { get; set; }
    public TadssDepositDto? Deposit { get; set; }
    public required string ReasonCode { get; set; }
    public required List<string> Reasons { get; set; }
    public required List<TadssRecommendationDto> Recommendations { get; set; }
    public DateTimeOffset? ApprovalExpiresAt { get; set; }
}
