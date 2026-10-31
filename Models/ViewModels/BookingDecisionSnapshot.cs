namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — mọi dữ liệu của booking mà request quyết định cọc TADSS cần, đọc một lần từ DB.
    /// </summary>
    /// <param name="CreatedAt">bookings.created_at (UTC).</param>
    /// <param name="FinalTotal">invoices.final_total: số tiền tính cọc, sau giảm giá.</param>
    /// <param name="PromotionCode">Mã khuyến mãi áp dụng (bản chụp lúc đặt); NULL khi không có hoặc khuyến mãi không có mã.</param>
    /// <param name="CustomerCreatedAt">users.created_at (UTC) của khách.</param>
    /// <param name="TierRank">Thứ hạng (1…n) của hạng hiện tại theo loyalty_tiers.min_points tăng dần; NULL khi khách chưa có hạng.</param>
    /// <param name="Courts">Các dòng sân, sắp theo (ngày, giờ bắt đầu, sân) để request tất định.</param>
    public sealed record BookingDecisionSnapshot(
        Guid BookingId,
        Guid BranchId,
        Guid CustomerId,
        DateTime CreatedAt,
        decimal FinalTotal,
        string? PromotionCode,
        DateTime CustomerCreatedAt,
        int? TierRank,
        IReadOnlyList<BookingDecisionCourt> Courts);
}
