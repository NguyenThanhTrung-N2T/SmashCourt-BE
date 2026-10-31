namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>
/// Body của <c>POST /api/v2/decisions/{id}/override</c> (override-request.schema.json).
/// <c>REJECT</c> phải bỏ hẳn khóa <c>deposit</c> (để null, không ghi ra JSON).
/// </summary>
public class TadssOverrideRequestDto
{
    public required TadssOverrideAction Action { get; set; }
    public TadssOverrideDepositDto? Deposit { get; set; }
    public required string Reason { get; set; }
}
