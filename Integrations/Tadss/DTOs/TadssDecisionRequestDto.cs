namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Body của <c>POST /api/v2/decisions</c> (decision-request.schema.json). Trường null không được ghi ra JSON.</summary>
public class TadssDecisionRequestDto
{
    /// <summary>Id booking của SmashCourt.</summary>
    public required Guid ReservationId { get; set; }
    public required Guid BranchId { get; set; }
    public required string BranchTimeZone { get; set; }
    public required List<TadssDecisionItemDto> Items { get; set; }
    public required TadssCustomerDto Customer { get; set; }
    /// <summary>Tổng tiền cần thanh toán (final_total của hóa đơn).</summary>
    public required decimal TotalPrice { get; set; }
    public required string Currency { get; set; }
    public required TadssChannel Channel { get; set; }
    public TadssDemandDto? Demand { get; set; }
    public required DateTimeOffset RequestedAt { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}
