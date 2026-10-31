namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Một sân trong đơn đặt (mỗi booking_court một item). Thời gian có offset.</summary>
public class TadssDecisionItemDto
{
    public required Guid ResourceId { get; set; }
    public required Guid ResourceTypeId { get; set; }
    public required DateTimeOffset StartTime { get; set; }
    public required DateTimeOffset EndTime { get; set; }
    /// <summary>Phí sân của sân này trước giảm giá.</summary>
    public required decimal Price { get; set; }
}
