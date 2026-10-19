using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class LoyaltyServiceTests
{
    [Fact]
    public async Task GetMyLoyaltyAsync_MissingLoyaltyThrowsNotFound()
    {
        var repository = new Mock<ICustomerLoyaltyRepository>();
        repository.Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>())).ReturnsAsync((CustomerLoyalty?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => new LoyaltyService(repository.Object, Mock.Of<ILoyaltyTierRepository>(), Mock.Of<ILoyaltyTransactionRepository>()).GetMyLoyaltyAsync(Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task GetMyLoyaltyAsync_BronzeTierCalculatesProgressToNextTier()
    {
        var loyalty = CreateLoyalty(totalPoints: 25, tierMinPoints: 0, tierName: "Bronze");
        var nextTier = CreateTier("Silver", 100, 5m);
        var repository = new Mock<ICustomerLoyaltyRepository>();
        var tiers = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetByUserIdAsync(loyalty.UserId)).ReturnsAsync(loyalty);
        tiers.Setup(x => x.GetNextTierAsync(0)).ReturnsAsync(nextTier);

        var result = await CreateService(repository, tiers).GetMyLoyaltyAsync(loyalty.UserId);

        Assert.Equal("Bronze", result.TierName);
        Assert.Equal("Silver", result.NextTierName);
        Assert.Equal(75, result.PointsToNextTier);
        Assert.Equal(25m, result.ProgressPercent);
        Assert.False(result.IsMaxTier);
    }

    [Fact]
    public async Task GetMyLoyaltyAsync_SilverTierCalculatesProgressToGoldTier()
    {
        var loyalty = CreateLoyalty(totalPoints: 150, tierMinPoints: 100, tierName: "Silver");
        var nextTier = CreateTier("Gold", 200, 10m);
        var repository = new Mock<ICustomerLoyaltyRepository>();
        var tiers = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetByUserIdAsync(loyalty.UserId)).ReturnsAsync(loyalty);
        tiers.Setup(x => x.GetNextTierAsync(100)).ReturnsAsync(nextTier);

        var result = await CreateService(repository, tiers).GetMyLoyaltyAsync(loyalty.UserId);

        Assert.Equal("Gold", result.NextTierName);
        Assert.Equal(50, result.PointsToNextTier);
        Assert.Equal(50m, result.ProgressPercent);
    }

    [Fact]
    public async Task GetMyLoyaltyAsync_WhenMaxTier_ReturnsNoNextTier()
    {
        var loyalty = CreateLoyalty(totalPoints: 500, tierMinPoints: 500, tierName: "Gold");
        var repository = new Mock<ICustomerLoyaltyRepository>();
        var tiers = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetByUserIdAsync(loyalty.UserId)).ReturnsAsync(loyalty);
        tiers.Setup(x => x.GetNextTierAsync(500)).ReturnsAsync((LoyaltyTier?)null);

        var result = await CreateService(repository, tiers).GetMyLoyaltyAsync(loyalty.UserId);

        Assert.True(result.IsMaxTier);
        Assert.Null(result.NextTierName);
        Assert.Null(result.PointsToNextTier);
        Assert.Null(result.ProgressPercent);
    }

    [Fact]
    public async Task GetMyLoyaltyAsync_WhenAtTierBoundary_ReturnsFullProgress()
    {
        var loyalty = CreateLoyalty(totalPoints: 100, tierMinPoints: 0, tierName: "Bronze");
        var nextTier = CreateTier("Silver", 100, 5m);
        var repository = new Mock<ICustomerLoyaltyRepository>();
        var tiers = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetByUserIdAsync(loyalty.UserId)).ReturnsAsync(loyalty);
        tiers.Setup(x => x.GetNextTierAsync(0)).ReturnsAsync(nextTier);

        var result = await CreateService(repository, tiers).GetMyLoyaltyAsync(loyalty.UserId);

        Assert.Equal(0, result.PointsToNextTier);
        Assert.Equal(100m, result.ProgressPercent);
    }

    [Fact]
    public async Task GetMyLoyaltyAsync_WhenUserHasZeroPoints_ReturnsZeroProgress()
    {
        var loyalty = CreateLoyalty(totalPoints: 0, tierMinPoints: 0, tierName: "Bronze");
        var nextTier = CreateTier("Silver", 100, 5m);
        var repository = new Mock<ICustomerLoyaltyRepository>();
        var tiers = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetByUserIdAsync(loyalty.UserId)).ReturnsAsync(loyalty);
        tiers.Setup(x => x.GetNextTierAsync(0)).ReturnsAsync(nextTier);

        var result = await CreateService(repository, tiers).GetMyLoyaltyAsync(loyalty.UserId);

        Assert.Equal(100, result.PointsToNextTier);
        Assert.Equal(0m, result.ProgressPercent);
    }

    [Fact]
    public async Task GetMyLoyaltyAsync_WhenTierRangeIsZero_ReturnsFullProgress()
    {
        var loyalty = CreateLoyalty(totalPoints: 50, tierMinPoints: 100, tierName: "Bronze");
        var nextTier = CreateTier("Silver", 100, 5m);
        var repository = new Mock<ICustomerLoyaltyRepository>();
        var tiers = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetByUserIdAsync(loyalty.UserId)).ReturnsAsync(loyalty);
        tiers.Setup(x => x.GetNextTierAsync(100)).ReturnsAsync(nextTier);

        var result = await CreateService(repository, tiers).GetMyLoyaltyAsync(loyalty.UserId);

        Assert.Equal(100, result.ProgressPercent);
    }

    [Fact]
    public async Task GetMyLoyaltyAsync_WhenProgressExceedsRange_ClampsTo100()
    {
        var loyalty = CreateLoyalty(totalPoints: 250, tierMinPoints: 0, tierName: "Bronze");
        var nextTier = CreateTier("Silver", 100, 5m);
        var repository = new Mock<ICustomerLoyaltyRepository>();
        var tiers = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetByUserIdAsync(loyalty.UserId)).ReturnsAsync(loyalty);
        tiers.Setup(x => x.GetNextTierAsync(0)).ReturnsAsync(nextTier);

        var result = await CreateService(repository, tiers).GetMyLoyaltyAsync(loyalty.UserId);

        Assert.Equal(100m, result.ProgressPercent);
        Assert.Equal(-150, result.PointsToNextTier);
    }

    [Fact]
    public async Task GetMyTransactionsAsync_WhenNoTransactions_ReturnsEmptyPagedResult()
    {
        var repository = new Mock<ILoyaltyTransactionRepository>();
        repository.Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>(), 1, 10))
            .ReturnsAsync((Array.Empty<LoyaltyTransaction>(), 0));

        var result = await CreateService(transactionRepository: repository)
            .GetMyTransactionsAsync(Guid.NewGuid(), new PaginationQuery { Page = 1, PageSize = 10 });

        Assert.Empty(result.Items);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(0, result.TotalItems);
    }

    [Fact]
    public async Task GetMyTransactionsAsync_WhenTransactionsExist_MapsPageAndTotal()
    {
        var bookingId = Guid.NewGuid();
        var transactions = new[]
        {
            new LoyaltyTransaction
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                Points = 20,
                TotalPointsAfter = 120,
                Type = LoyaltyTransactionType.EARN,
                Note = "Booking",
                CreatedAt = DateTime.UtcNow,
                Booking = new Booking { Id = bookingId, BookingCode = "BK-001" }
            }
        };
        var repository = new Mock<ILoyaltyTransactionRepository>();
        repository.Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>(), 1, 10))
            .ReturnsAsync((transactions, 21));

        var result = await CreateService(transactionRepository: repository)
            .GetMyTransactionsAsync(Guid.NewGuid(), new PaginationQuery { Page = 1, PageSize = 10 });
        var item = Assert.Single(result.Items);

        Assert.Equal("BK-001", item.BookingCode);
        Assert.Equal(20, item.Points);
        Assert.Equal(120, item.TotalPointsAfter);
        Assert.Equal("EARN", item.Type);
        Assert.Equal(21, result.TotalItems);
    }

    [Fact]
    public async Task GetMyTransactionsAsync_WhenPageTwo_PassesPageParametersToRepository()
    {
        var repository = new Mock<ILoyaltyTransactionRepository>();
        repository.Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>(), 2, 5))
            .ReturnsAsync((Array.Empty<LoyaltyTransaction>(), 6));

        var result = await CreateService(transactionRepository: repository)
            .GetMyTransactionsAsync(Guid.NewGuid(), new PaginationQuery { Page = 2, PageSize = 5 });

        Assert.Equal(2, result.Page);
        Assert.Equal(5, result.PageSize);
        Assert.Equal(6, result.TotalItems);
        repository.Verify(x => x.GetByUserIdAsync(It.IsAny<Guid>(), 2, 5), Times.Once);
    }

    [Fact]
    public async Task GetMyTransactionsAsync_WhenBookingIsNull_UsesEmptyBookingCode()
    {
        var repository = new Mock<ILoyaltyTransactionRepository>();
        repository.Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>(), 1, 10))
            .ReturnsAsync((new[] { new LoyaltyTransaction { Type = LoyaltyTransactionType.DEDUCT } }, 1));

        var result = await CreateService(transactionRepository: repository)
            .GetMyTransactionsAsync(Guid.NewGuid(), new PaginationQuery { Page = 1, PageSize = 10 });

        Assert.Equal(string.Empty, Assert.Single(result.Items).BookingCode);
    }

    [Fact]
    public async Task GetMyTransactionsAsync_MapsDeductTransactionAndTotalPointsAfter()
    {
        var transaction = new LoyaltyTransaction
        {
            Points = -10,
            TotalPointsAfter = 90,
            Type = LoyaltyTransactionType.DEDUCT
        };
        var repository = new Mock<ILoyaltyTransactionRepository>();
        repository.Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>(), 1, 10))
            .ReturnsAsync((new[] { transaction }, 1));

        var result = await CreateService(transactionRepository: repository)
            .GetMyTransactionsAsync(Guid.NewGuid(), new PaginationQuery { Page = 1, PageSize = 10 });
        var item = Assert.Single(result.Items);

        Assert.Equal(-10, item.Points);
        Assert.Equal(90, item.TotalPointsAfter);
        Assert.Equal("DEDUCT", item.Type);
    }

    private static LoyaltyService CreateService(
        Mock<ICustomerLoyaltyRepository>? loyaltyRepository = null,
        Mock<ILoyaltyTierRepository>? tierRepository = null,
        Mock<ILoyaltyTransactionRepository>? transactionRepository = null) =>
        new(
            (loyaltyRepository ?? new Mock<ICustomerLoyaltyRepository>()).Object,
            (tierRepository ?? new Mock<ILoyaltyTierRepository>()).Object,
            (transactionRepository ?? new Mock<ILoyaltyTransactionRepository>()).Object);

    private static CustomerLoyalty CreateLoyalty(int totalPoints, int tierMinPoints, string tierName) => new()
    {
        UserId = Guid.NewGuid(),
        TotalPoints = totalPoints,
        Tier = CreateTier(tierName, tierMinPoints, 0m)
    };

    private static LoyaltyTier CreateTier(string name, int minPoints, decimal discountRate) => new()
    {
        Name = name,
        MinPoints = minPoints,
        DiscountRate = discountRate
    };
}
