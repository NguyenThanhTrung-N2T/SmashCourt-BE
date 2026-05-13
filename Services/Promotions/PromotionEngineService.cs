using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.Promotions
{
    public class PromotionEngineService
    {
        private readonly SmashCourtContext _context;

        public PromotionEngineService(SmashCourtContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Validates a promotion code and calculates discount
        /// </summary>
        public async Task<PromotionValidationResult> ValidatePromotionAsync(string code, PromotionContext context)
        {
            // 1. Find promotion by code
            var promotion = await _context.Promotions
                .Include(p => p.Conditions)
                .FirstOrDefaultAsync(p => p.Code == code && p.Status == PromotionStatus.ACTIVE);

            if (promotion == null)
            {
                return new PromotionValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Mã khuyến mãi không tồn tại hoặc đã hết hạn"
                };
            }

            // 2. Check date validity
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today < promotion.StartDate || today > promotion.EndDate)
            {
                return new PromotionValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Mã khuyến mãi chưa có hiệu lực hoặc đã hết hạn"
                };
            }

            // 3. Check usage limit
            if (promotion.UsageLimit.HasValue && promotion.UsedCount >= promotion.UsageLimit.Value)
            {
                return new PromotionValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Mã khuyến mãi đã hết lượt sử dụng"
                };
            }

            // 4. Check per-user usage limit
            if (promotion.UsagePerUserLimit.HasValue)
            {
                var userUsageCount = await _context.BookingPromotions
                    .Where(bp => bp.PromotionId == promotion.Id)
                    .Join(_context.Bookings,
                        bp => bp.BookingId,
                        b => b.Id,
                        (bp, b) => new { bp, b })
                    .Where(x => x.b.CustomerId == context.UserId)
                    .CountAsync();

                if (userUsageCount >= promotion.UsagePerUserLimit.Value)
                {
                    return new PromotionValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Bạn đã sử dụng hết lượt áp dụng mã khuyến mãi này"
                    };
                }
            }

            // 5. Evaluate conditions
            var conditionResult = EvaluateConditions(promotion, context);
            if (!conditionResult.IsValid)
            {
                return conditionResult;
            }

            // 6. Calculate discount
            var discountAmount = CalculateDiscount(promotion, context.BookingAmount);
            var finalAmount = Math.Max(0, context.BookingAmount - discountAmount);

            return new PromotionValidationResult
            {
                IsValid = true,
                Promotion = promotion,
                DiscountAmount = discountAmount,
                FinalAmount = finalAmount
            };
        }

        /// <summary>
        /// Calculates discount amount based on promotion type
        /// </summary>
        public decimal CalculateDiscount(Promotion promotion, decimal amount)
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

        /// <summary>
        /// Evaluates all promotion conditions
        /// </summary>
        private PromotionValidationResult EvaluateConditions(Promotion promotion, PromotionContext context)
        {
            foreach (var condition in promotion.Conditions)
            {
                switch (condition.ConditionType)
                {
                    case "MIN_BOOKING_AMOUNT":
                        if (!decimal.TryParse(condition.ConditionValue, out var minAmount) || context.BookingAmount < minAmount)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Giá trị đơn hàng tối thiểu phải từ {minAmount:N0} VNĐ"
                            };
                        }
                        break;

                    case "MAX_PREVIOUS_BOOKINGS":
                        if (!int.TryParse(condition.ConditionValue, out var maxBookings) || context.PreviousBookingCount > maxBookings)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Khuyến mãi này chỉ dành cho khách hàng mới"
                            };
                        }
                        break;

                    case "BRANCH_ID":
                        if (context.BranchId.ToString() != condition.ConditionValue)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Mã khuyến mãi không áp dụng cho chi nhánh này"
                            };
                        }
                        break;

                    case "COURT_ID":
                        if (context.CourtId.ToString() != condition.ConditionValue)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Mã khuyến mãi không áp dụng cho sân này"
                            };
                        }
                        break;

                    case "SPORT":
                        if (context.Sport != condition.ConditionValue)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng cho môn {condition.ConditionValue}"
                            };
                        }
                        break;

                    case "DAY_OF_WEEK":
                        var dayOfWeek = context.BookingDate.DayOfWeek.ToString().ToUpper();
                        if (dayOfWeek != condition.ConditionValue.ToUpper())
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng vào {condition.ConditionValue}"
                            };
                        }
                        break;

                    case "START_HOUR":
                        if (!int.TryParse(condition.ConditionValue, out var startHour) || context.BookingDate.Hour < startHour)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng từ {startHour}h trở đi"
                            };
                        }
                        break;

                    case "END_HOUR":
                        if (!int.TryParse(condition.ConditionValue, out var endHour) || context.BookingDate.Hour >= endHour)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng trước {endHour}h"
                            };
                        }
                        break;

                    case "MONTH":
                        if (!int.TryParse(condition.ConditionValue, out var month) || context.BookingDate.Month != month)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng trong tháng {month}"
                            };
                        }
                        break;

                    case "DAYS_OF_MONTH":
                        if (!int.TryParse(condition.ConditionValue, out var dayOfMonth) || context.BookingDate.Day != dayOfMonth)
                        {
                            return new PromotionValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = $"Mã khuyến mãi chỉ áp dụng vào ngày {dayOfMonth} trong tháng"
                            };
                        }
                        break;

                    default:
                        // Unknown condition type - skip
                        break;
                }
            }

            return new PromotionValidationResult { IsValid = true };
        }

        /// <summary>
        /// Increments the used count for a promotion
        /// </summary>
        public async Task IncrementUsageCountAsync(Guid promotionId)
        {
            var promotion = await _context.Promotions.FindAsync(promotionId);
            if (promotion != null)
            {
                promotion.UsedCount++;
                await _context.SaveChangesAsync();
            }
        }
    }
}
