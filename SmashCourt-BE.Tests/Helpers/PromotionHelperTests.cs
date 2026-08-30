using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Helpers;

[Trait("Category", TestCategories.Promotion)]
[Trait("Category", TestCategories.Pricing)]
[Trait("Category", TestCategories.Helper)]
public class PromotionHelperTests
{
    [Fact]
    public void CalculateDiscount_WhenPercentage_ReturnsRoundedDiscount()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.PERCENT, 
            discountValue: 12.5m);

        var result = PromotionHelper.CalculateDiscount(promotion, TestConstants.StandardBookingAmount);

        Assert.Equal(125_000m, result);
    }

    [Fact]
    public void CalculateDiscount_WhenPercentageWithMaxCap_AppliesMaximum()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.PERCENT, 
            discountValue: 20m, 
            maxDiscountAmount: 100_000m);

        var result = PromotionHelper.CalculateDiscount(promotion, TestConstants.StandardBookingAmount);

        Assert.Equal(100_000m, result);
    }

    [Fact]
    public void CalculateDiscount_WhenPercentageWithoutMaxCap_AppliesFullDiscount()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.PERCENT, 
            discountValue: 50m);

        var result = PromotionHelper.CalculateDiscount(promotion, TestConstants.StandardBookingAmount);

        Assert.Equal(500_000m, result);
    }

    [Fact]
    public void CalculateDiscount_WhenFixedAmount_ReturnsFixedDiscount()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.FIXED, 
            discountValue: TestConstants.StandardFixedDiscount);

        var result = PromotionHelper.CalculateDiscount(promotion, TestConstants.StandardBookingAmount);

        Assert.Equal(TestConstants.StandardFixedDiscount, result);
    }

    [Fact]
    public void CalculateDiscount_WhenFixedExceedsOrderAmount_CapsAtOrderAmount()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.FIXED, 
            discountValue: 2_000_000m);

        var result = PromotionHelper.CalculateDiscount(promotion, 500_000m);

        Assert.Equal(500_000m, result);
    }

    [Fact]
    public void CalculateDiscount_WhenFixedEqualsOrderAmount_ReturnsFullAmount()
    {
        var orderAmount = 100_000m;
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.FIXED, 
            discountValue: orderAmount);

        var result = PromotionHelper.CalculateDiscount(promotion, orderAmount);

        Assert.Equal(orderAmount, result);
    }

    [Fact]
    public void CalculateDiscount_WhenZeroOrderAmount_ReturnsZero()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.PERCENT, 
            discountValue: 50m);

        var result = PromotionHelper.CalculateDiscount(promotion, 0m);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateDiscount_WhenAmountIsNegative_ReturnsZero()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.FIXED,
            discountValue: 100_000m);

        var result = PromotionHelper.CalculateDiscount(promotion, -50_000m);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateDiscount_WhenDiscountValueIsNegative_ReturnsZero()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.FIXED,
            discountValue: -100_000m);

        var result = PromotionHelper.CalculateDiscount(promotion, 500_000m);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateDiscount_WhenVeryLargeAmount_HandlesCorrectly()
    {
        var promotion = TestDataFactory.CreatePromotion(
            discountType: DiscountTypeEnum.PERCENT, 
            discountValue: 10m,
            maxDiscountAmount: TestConstants.MaxDiscountAmount);

        var result = PromotionHelper.CalculateDiscount(promotion, TestConstants.MaximumBookingAmount);

        Assert.Equal(TestConstants.MaxDiscountAmount, result);
    }
}
