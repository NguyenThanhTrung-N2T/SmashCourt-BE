namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Response 200 của override (override-response.schema.json); cũng là phần <c>override</c> của chi tiết quyết định.</summary>
public class TadssOverrideResponseDto
{
    public required Guid DecisionId { get; set; }
    public required TadssOverrideAction Action { get; set; }
    /// <summary>Đã quy đổi cả rate và amount; null khi REJECT.</summary>
    public TadssDepositDto? Deposit { get; set; }
    public required string ActorId { get; set; }
    public required string Reason { get; set; }
    public required DateTimeOffset OverriddenAt { get; set; }
}
