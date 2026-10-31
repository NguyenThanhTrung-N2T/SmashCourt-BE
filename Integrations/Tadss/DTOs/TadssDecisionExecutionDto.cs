namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Phần <c>execution</c> của chi tiết quyết định.</summary>
public class TadssDecisionExecutionDto
{
    public required string StrategyName { get; set; }
    public required string StrategyVersion { get; set; }
    public required string ExecutionType { get; set; }
    public required int ExecutionTimeMs { get; set; }
    public required bool FallbackUsed { get; set; }
    public string? FailureReason { get; set; }
    public required bool CacheHit { get; set; }
}
