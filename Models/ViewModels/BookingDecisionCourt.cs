namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — một dòng sân của booking dùng dựng item gửi TADSS. Ngày và giờ là giờ tường VN.
    /// </summary>
    /// <param name="Price">Phí sân của dòng này trước giảm giá (tổng booking_price_items.unit_price).</param>
    public sealed record BookingDecisionCourt(
        Guid CourtId,
        Guid CourtTypeId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        decimal Price);
}
