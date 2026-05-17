using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Promotions;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Services;

/// <summary>
/// Engine validate mã khuyến mãi và tính discount.
/// Tuân thủ kiến trúc layer: Service → Repository → DbContext.
/// </summary>
public class PromotionEngineService
{
    private readonly IPromotionRepository _promotionRepo;

    public PromotionEngineService(IPromotionRepository promotionRepo)
    {
        _promotionRepo = promotionRepo;
    }

    /// <summary>
    /// Validates a promotion code và tính discount amount.
    /// </summary>
    public async Task<PromotionValidationResult> ValidatePromotionAsync(string code, PromotionContext context)
    {
        // 1. Tìm promotion theo code + status ACTIVE (kèm conditions)
        var promotion = await _promotionRepo.GetByCodeActiveAsync(code);

        if (promotion == null)
            return Fail("Mã khuyến mãi không tồn tại hoặc đã hết hạn");

        // 2. Kiểm tra ngày hiệu lực
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today < promotion.StartDate || today > promotion.EndDate)
            return Fail("Mã khuyến mãi chưa có hiệu lực hoặc đã hết hạn");

        // 3. Kiểm tra tổng lượt sử dụng
        if (promotion.UsageLimit.HasValue && promotion.UsedCount >= promotion.UsageLimit.Value)
            return Fail("Mã khuyến mãi đã hết lượt sử dụng");

        // 4. Kiểm tra lượt sử dụng theo user
        if (promotion.UsagePerUserLimit.HasValue)
        {
            var userUsageCount = await _promotionRepo.GetUserUsageCountAsync(promotion.Id, context.UserId);
            if (userUsageCount >= promotion.UsagePerUserLimit.Value)
                return Fail("Bạn đã sử dụng hết lượt áp dụng mã khuyến mãi này");
        }

        // 5. Đánh giá các conditions
        var conditionResult = EvaluateConditions(promotion, context);
        if (!conditionResult.IsValid)
            return conditionResult;

        // 6. Tính discount — dùng PromotionHelper (single source of truth)
        var discountAmount = PromotionHelper.CalculateDiscount(promotion, context.BookingAmount);
        var finalAmount    = Math.Max(0, context.BookingAmount - discountAmount);

        return new PromotionValidationResult
        {
            IsValid        = true,
            Promotion      = promotion,
            DiscountAmount = discountAmount,
            FinalAmount    = finalAmount
        };
    }

    /// <summary>
    /// Tăng UsedCount — delegate xuống Repository layer.
    /// </summary>
    public async Task IncrementUsageCountAsync(Guid promotionId)
    {
        await _promotionRepo.IncrementUsageCountAsync(promotionId);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static PromotionValidationResult EvaluateConditions(
        Models.Entities.Promotion promotion, PromotionContext context)
    {
        foreach (var condition in promotion.Conditions)
        {
            switch (condition.ConditionType)
            {
                case "MIN_BOOKING_AMOUNT":
                    if (!decimal.TryParse(condition.ConditionValue, out var minAmount)
                        || context.BookingAmount < minAmount)
                        return Fail($"Giá trị đơn hàng tối thiểu phải từ {minAmount:N0} VNĐ");
                    break;

                case "MAX_PREVIOUS_BOOKINGS":
                    if (!int.TryParse(condition.ConditionValue, out var maxBookings)
                        || context.PreviousBookingCount > maxBookings)
                        return Fail("Khuyến mãi này chỉ dành cho khách hàng mới");
                    break;

                case "BRANCH_ID":
                    if (context.BranchId.ToString() != condition.ConditionValue)
                        return Fail("Mã khuyến mãi không áp dụng cho chi nhánh này");
                    break;

                case "COURT_ID":
                    if (context.CourtId.ToString() != condition.ConditionValue)
                        return Fail("Mã khuyến mãi không áp dụng cho sân này");
                    break;

                case "SPORT":
                    if (context.Sport != condition.ConditionValue)
                        return Fail($"Mã khuyến mãi chỉ áp dụng cho môn {condition.ConditionValue}");
                    break;

                case "DAY_OF_WEEK":
                    var dayOfWeek = context.BookingDate.DayOfWeek.ToString().ToUpper();
                    if (dayOfWeek != condition.ConditionValue.ToUpper())
                        return Fail($"Mã khuyến mãi chỉ áp dụng vào {condition.ConditionValue}");
                    break;

                case "START_HOUR":
                    if (!int.TryParse(condition.ConditionValue, out var startHour)
                        || context.BookingDate.Hour < startHour)
                        return Fail($"Mã khuyến mãi chỉ áp dụng từ {startHour}h trở đi");
                    break;

                case "END_HOUR":
                    if (!int.TryParse(condition.ConditionValue, out var endHour)
                        || context.BookingDate.Hour >= endHour)
                        return Fail($"Mã khuyến mãi chỉ áp dụng trước {endHour}h");
                    break;

                case "MONTH":
                    if (!int.TryParse(condition.ConditionValue, out var month)
                        || context.BookingDate.Month != month)
                        return Fail($"Mã khuyến mãi chỉ áp dụng trong tháng {month}");
                    break;

                case "DAYS_OF_MONTH":
                    if (!int.TryParse(condition.ConditionValue, out var dayOfMonth)
                        || context.BookingDate.Day != dayOfMonth)
                        return Fail($"Mã khuyến mãi chỉ áp dụng vào ngày {dayOfMonth} trong tháng");
                    break;

                default:
                    // Unknown condition — skip (forward-compatible)
                    break;
            }
        }

        return new PromotionValidationResult { IsValid = true };
    }

    private static PromotionValidationResult Fail(string message) =>
        new() { IsValid = false, ErrorMessage = message };
}
