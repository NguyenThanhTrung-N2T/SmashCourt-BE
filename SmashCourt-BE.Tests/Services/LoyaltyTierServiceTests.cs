using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.LoyaltyTier;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class LoyaltyTierServiceTests
{
    [Fact]
    public async Task GetLoyaltyTierByIdAsync_MissingTierThrowsNotFound()
    {
        var repository = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetLoyaltyTierByIdAsync(It.IsAny<Guid>())).ReturnsAsync((LoyaltyTier?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => new LoyaltyTierService(repository.Object, Mock.Of<ICustomerLoyaltyRepository>()).GetLoyaltyTierByIdAsync(Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task UpdateLoyaltyTierAsync_BronzeMustStartAtZero()
    {
        var tier = new LoyaltyTier { Id = Guid.NewGuid(), Name = "Bronze", MinPoints = 0 };
        var repository = new Mock<ILoyaltyTierRepository>();
        repository.Setup(x => x.GetLoyaltyTierByIdAsync(tier.Id)).ReturnsAsync(tier);

        var exception = await Assert.ThrowsAsync<AppException>(() => new LoyaltyTierService(repository.Object, Mock.Of<ICustomerLoyaltyRepository>()).UpdateLoyaltyTierAsync(
            tier.Id, new UpdateLoyaltyTierDto { MinPoints = 1 }));

        Assert.Equal(400, exception.StatusCode);
    }
}
