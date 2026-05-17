using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.Promotions
{
    /// <summary>
    /// Helper class for promotion discount calculations
    /// </summary>
    public static class PromotionHelper
    {
        /// <summary>
        /// Calculates discount amount based on promotion type
        /// </summary>
        public static decimal CalculateDiscount(Promotion promotion, decimal amount)
        {
            decimal discount;

            if (promotion.DiscountType == DiscountTypeEnum.PERCENT)
            {
                discount = amount * promotion.DiscountValue / 100;

                // Apply max discount cap if specified
                if (promotion.MaxDiscountAmount.HasValue)
                {
                    discount = Math.Min(discount, promotion.MaxDiscountAmount.Value);
                }
            }
            else // FIXED
            {
                discount = promotion.DiscountValue;
            }

            // Discount cannot exceed the amount
            discount = Math.Min(discount, amount);

            return Math.Round(discount, 0);
        }
    }
}