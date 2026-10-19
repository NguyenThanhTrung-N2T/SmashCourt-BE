using Moq;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Promotions;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

[Trait("Category", TestCategories.Promotion)]
public class PromotionEngineServiceTests
{
    private readonly Mock<IPromotionRepository> _repository = new();

    [Fact]
    public async Task ValidatePromotionAsync_WhenCodeDoesNotExist_ReturnsInvalidResult()
    {
        _repository.Setup(x => x.GetByCodeNotDeletedAsync("MISSING"))
            .ReturnsAsync((Promotion?)null);

        var result = await CreateService().ValidatePromotionAsync("MISSING", TestDataFactory.CreatePromotionContext());

        Assert.False(result.IsValid);
        Assert.Contains("không tồn tại", result.ErrorMessage);
        Assert.Null(result.Promotion);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenPromotionExpired_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        var context = TestDataFactory.CreatePromotionContext(
            bookingDate: new DateTime(2027, 1, 1)); // After promotion end date

        var result = await CreateService().ValidatePromotionDirectAsync(promotion, context);

        Assert.False(result.IsValid);
        Assert.Null(result.Promotion);
        Assert.Contains("hết hạn", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenPromotionNotStarted_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion(
            startDate: new DateOnly(2026, 12, 1),
            endDate: new DateOnly(2026, 12, 31));
        var context = TestDataFactory.CreatePromotionContext(
            bookingDate: new DateTime(2026, 1, 1)); // Before promotion start date

        var result = await CreateService().ValidatePromotionDirectAsync(promotion, context);

        Assert.False(result.IsValid);
        Assert.Null(result.Promotion);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenConditionsPass_CalculatesFinalAmount()
    {
        var promotion = TestDataFactory.CreatePromotion(discountValue: 10m);
        promotion.Conditions.Add(new PromotionCondition 
        { 
            ConditionType = "MIN_BOOKING_AMOUNT", 
            ConditionValue = "500000" 
        });
        var context = TestDataFactory.CreatePromotionContext(
            bookingAmount: TestConstants.StandardBookingAmount);

        var result = await CreateService().ValidatePromotionDirectAsync(promotion, context);

        Assert.True(result.IsValid);
        Assert.Equal(100_000m, result.DiscountAmount);
        Assert.Equal(900_000m, result.FinalAmount);
        Assert.NotNull(result.Promotion);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenUsageLimitReached_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion(
            usageLimit: 10,
            usedCount: 10);

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, 
            TestDataFactory.CreatePromotionContext());

        Assert.False(result.IsValid);
        Assert.Contains("hết lượt", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenUsageLimitNotReached_ReturnsValid()
    {
        var promotion = TestDataFactory.CreatePromotion(
            usageLimit: 100,
            usedCount: 50);

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, 
            TestDataFactory.CreatePromotionContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenNoUsageLimit_ReturnsValid()
    {
        var promotion = TestDataFactory.CreatePromotion(
            usageLimit: null,
            usedCount: 1000);

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, 
            TestDataFactory.CreatePromotionContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionAsync_WhenPromotionCodeIsWhitespace_ReturnsInvalidResult()
    {
        var result = await CreateService().ValidatePromotionAsync(
            "   ", 
            TestDataFactory.CreatePromotionContext());

        Assert.False(result.IsValid);
        _repository.Verify(x => x.GetByCodeNotDeletedAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ValidatePromotionAsync_WhenUserUsageLimitIsReached_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.UsagePerUserLimit = 2;
        var context = TestDataFactory.CreatePromotionContext(userId: Guid.NewGuid());
        var code = promotion.Code!;
        _repository.Setup(x => x.GetByCodeNotDeletedAsync(code)).ReturnsAsync(promotion);
        _repository.Setup(x => x.GetUserUsageCountAsync(promotion.Id, context.UserId)).ReturnsAsync(2);

        var result = await CreateService().ValidatePromotionAsync(code, context);

        Assert.False(result.IsValid);
        Assert.Contains("hết lượt", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePromotionAsync_WhenAllValidationsPass_ReturnsCalculatedResult()
    {
        var promotion = TestDataFactory.CreatePromotion(code: "VALID");
        _repository.Setup(x => x.GetByCodeNotDeletedAsync("VALID")).ReturnsAsync(promotion);

        var result = await CreateService().ValidatePromotionAsync(
            "VALID", TestDataFactory.CreatePromotionContext());

        Assert.True(result.IsValid);
        Assert.Same(promotion, result.Promotion);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenBookingAmountIsBelowMinimum_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MIN_BOOKING_AMOUNT",
            ConditionValue = "1000000"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(bookingAmount: 999999m));

        Assert.False(result.IsValid);
        Assert.Contains("tối thiểu", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenBookingAmountEqualsMinimum_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MIN_BOOKING_AMOUNT",
            ConditionValue = "1000000"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(bookingAmount: 1000000m));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenMinimumAmountIsInvalid_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MIN_BOOKING_AMOUNT",
            ConditionValue = "invalid"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenPreviousBookingsAreWithinLimit_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MAX_PREVIOUS_BOOKINGS",
            ConditionValue = "2"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(previousBookingCount: 2));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenPreviousBookingsExceedLimit_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MAX_PREVIOUS_BOOKINGS",
            ConditionValue = "2"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(previousBookingCount: 3));

        Assert.False(result.IsValid);
        Assert.Contains("khách hàng mới", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenMaximumPreviousBookingsIsInvalid_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MAX_PREVIOUS_BOOKINGS",
            ConditionValue = "invalid"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenBranchMatches_ReturnsValidResult()
    {
        var branchId = Guid.NewGuid();
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "BRANCH_ID",
            ConditionValue = branchId.ToString()
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(branchId: branchId));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenBranchDoesNotMatch_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "BRANCH_ID",
            ConditionValue = Guid.NewGuid().ToString()
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(branchId: Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.Contains("chi nhánh", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenDayOfWeekMatchesIgnoringCase_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "DAY_OF_WEEK",
            ConditionValue = "monday"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 5)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenDayOfWeekDoesNotMatch_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "DAY_OF_WEEK",
            ConditionValue = "MONDAY"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 6)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenStartTimeIsBeforeLimit_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "START_HOUR",
            ConditionValue = "18:00"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(startTime: new TimeSpan(17, 59, 0)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenStartTimeEqualsLimit_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "START_HOUR",
            ConditionValue = "18:00"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(startTime: new TimeSpan(18, 0, 0)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenEndTimeExceedsLimit_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "END_HOUR",
            ConditionValue = "20:00"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(endTime: new TimeSpan(20, 1, 0)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenTimeFormatIsInvalid_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "START_HOUR",
            ConditionValue = "not-a-time"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext());

        Assert.False(result.IsValid);
        Assert.Contains("không hợp lệ", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenMonthMatches_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MONTH",
            ConditionValue = "1"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 15)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenMonthDoesNotMatch_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "MONTH",
            ConditionValue = "2"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 15)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenDayOfMonthMatches_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "DAYS_OF_MONTH",
            ConditionValue = "15"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 15)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenDayOfMonthDoesNotMatch_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "DAYS_OF_MONTH",
            ConditionValue = "15"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 16)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenSpecificDateIsInList_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "SPECIFIC_DATES",
            ConditionValue = "01/01, 15/01"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 15)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenSpecificDateIsNotInList_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "SPECIFIC_DATES",
            ConditionValue = "01/01, 15/01"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 16)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenUnknownConditionIsUsed_SkipsCondition()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition
        {
            ConditionType = "FUTURE_CONDITION",
            ConditionValue = "anything"
        });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext());

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenMultipleConditionsAllMatch_ReturnsValidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition { ConditionType = "MONTH", ConditionValue = "1" });
        promotion.Conditions.Add(new PromotionCondition { ConditionType = "DAYS_OF_MONTH", ConditionValue = "15" });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 15)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenMultipleConditionsOneFails_ReturnsInvalidResult()
    {
        var promotion = TestDataFactory.CreatePromotion();
        promotion.Conditions.Add(new PromotionCondition { ConditionType = "MONTH", ConditionValue = "1" });
        promotion.Conditions.Add(new PromotionCondition { ConditionType = "DAYS_OF_MONTH", ConditionValue = "16" });

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(
                bookingDate: new DateTime(2026, 1, 15)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenFixedDiscountIsUsed_ReturnsFixedDiscount()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.FIXED,
            discountValue: 250000m);

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext());

        Assert.True(result.IsValid);
        Assert.Equal(250000m, result.DiscountAmount);
        Assert.Equal(750000m, result.FinalAmount);
    }

    [Fact]
    public async Task ValidatePromotionDirectAsync_WhenDiscountExceedsBookingAmount_ClampsFinalAmountToZero()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.FIXED,
            discountValue: 2000000m);

        var result = await CreateService().ValidatePromotionDirectAsync(
            promotion, TestDataFactory.CreatePromotionContext(bookingAmount: 500000m));

        Assert.True(result.IsValid);
        Assert.Equal(500000m, result.DiscountAmount);
        Assert.Equal(0m, result.FinalAmount);
    }

    [Fact]
    public async Task IncrementUsageCountAsync_DelegatesToRepository()
    {
        var promotionId = Guid.NewGuid();

        await CreateService().IncrementUsageCountAsync(promotionId);

        _repository.Verify(x => x.IncrementUsageCountAsync(promotionId), Times.Once);
    }

    [Fact]
    public async Task DecrementUsageCountAsync_DelegatesToRepository()
    {
        var promotionId = Guid.NewGuid();

        await CreateService().DecrementUsageCountAsync(promotionId);

        _repository.Verify(x => x.DecrementUsageCountAsync(promotionId), Times.Once);
    }

    private PromotionEngineService CreateService() => new(_repository.Object);
}
