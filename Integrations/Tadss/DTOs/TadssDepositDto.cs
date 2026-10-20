namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Mức cọc TADSS trả về (rate trong [0, 1], amount theo currency).</summary>
public class TadssDepositDto
{
    public required decimal Rate { get; set; }
    public required decimal Amount { get; set; }
    public required string Currency { get; set; }
}
