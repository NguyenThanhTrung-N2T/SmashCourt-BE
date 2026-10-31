namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Mức cọc Manager chọn khi APPROVE: đúng một trong <c>Rate</c> hoặc <c>Amount</c>.</summary>
public class TadssOverrideDepositDto
{
    public decimal? Rate { get; set; }
    public decimal? Amount { get; set; }
}
