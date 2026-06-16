using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Models.Promotions;

/// <summary>
/// Kết quả validate mã khuyến mãi từ PromotionEngineService.
/// Đây là domain model — thuộc Models layer.
/// </summary>
public class PromotionValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public Promotion? Promotion { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
}
