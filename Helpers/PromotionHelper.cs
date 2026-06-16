using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Helpers;

/// <summary>
/// Static helper: tính discount amount từ promotion entity.
/// Dùng chung cho PromotionEngineService và bất kỳ nơi nào cần tính discount.
/// </summary>
public static class PromotionHelper
{
    /// <summary>
    /// Tính số tiền giảm giá dựa trên loại promotion (PERCENT / FIXED).
    /// Kết quả không vượt quá <paramref name="amount"/> và được làm tròn đến đơn vị.
    /// </summary>
    public static decimal CalculateDiscount(Promotion promotion, decimal amount)
    {
        decimal discount;

        if (promotion.DiscountType == DiscountTypeEnum.PERCENT)
        {
            discount = amount * promotion.DiscountValue / 100;

            // Apply max discount cap if specified
            if (promotion.MaxDiscountAmount.HasValue)
                discount = Math.Min(discount, promotion.MaxDiscountAmount.Value);
        }
        else // FIXED
        {
            discount = promotion.DiscountValue;
        }

        // Discount cannot exceed the total amount
        discount = Math.Min(discount, amount);

        return Math.Round(discount, 0);
    }
}
