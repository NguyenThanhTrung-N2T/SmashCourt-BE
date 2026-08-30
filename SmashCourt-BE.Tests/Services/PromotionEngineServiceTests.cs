using Moq;
using SmashCourt_BE.Models.Entities;
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

    private PromotionEngineService CreateService() => new(_repository.Object);
}
