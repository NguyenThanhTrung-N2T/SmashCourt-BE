namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO báo cáo hiệu quả khuyến mãi
/// </summary>
public class PromotionEffectivenessReportDto
{
    public decimal TotalDiscountAmount { get; set; }
    public int TotalPromotionUsage { get; set; }
    public decimal AverageDiscountPerUsage { get; set; }
    public decimal PromotionConversionRate { get; set; }
    public List<PromotionItemDto> TopPromotions { get; set; } = [];
    public List<PromotionTrendDto> PromotionTrend { get; set; } = [];
}

/// <summary>
/// Chi tiết khuyến mãi
/// </summary>
public class PromotionItemDto
{
    public Guid PromotionId { get; set; }
    public string PromotionName { get; set; } = null!;
    public string PromotionCode { get; set; } = null!;
    public int UsageCount { get; set; }
    public decimal TotalDiscount { get; set; }
    public decimal RevenueAfterDiscount { get; set; }
    public decimal AverageDiscount { get; set; }
}

/// <summary>
/// Xu hướng sử dụng khuyến mãi
/// </summary>
public class PromotionTrendDto
{
    public string Period { get; set; } = null!;
    public int UsageCount { get; set; }
    public decimal TotalDiscount { get; set; }
}
