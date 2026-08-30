using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
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
}
