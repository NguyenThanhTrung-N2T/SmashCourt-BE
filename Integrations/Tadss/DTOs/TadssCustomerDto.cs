namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Khách đặt sân. BE luôn gửi <c>IsGuest = false</c> vì đặt online bắt buộc đăng nhập.</summary>
public class TadssCustomerDto
{
    public Guid? CustomerId { get; set; }
    public required bool IsGuest { get; set; }
    public required TadssTier Tier { get; set; }
    public DateTimeOffset? MemberSince { get; set; }
    public TadssHistoryDto? History { get; set; }
}
