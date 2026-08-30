using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

public class ProfileServiceTests
{
    [Fact]
    public async Task GetMyProfileAsync_MissingUserThrowsUserNotFound()
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(x => x.GetUserByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SmashCourt_BE.Models.Entities.User?)null);
        var service = new ProfileService(repository.Object, Mock.Of<IRefreshTokenRepository>(), Mock.Of<ICustomerLoyaltyRepository>(), Mock.Of<IUserBranchRepository>(), Mock.Of<ILoyaltyTierRepository>(), TestConfigurationFactory.Create(), Mock.Of<ILogger<ProfileService>>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetMyProfileAsync(Guid.NewGuid()));

        Assert.Equal(ErrorCodes.UserNotFound, exception.ErrorCode);
    }
}
