using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

public class UserManagementServiceTests
{
    [Fact]
    public async Task GetUserByIdAsync_ManagerCannotAccessMissingUser()
    {
        var users = new Mock<IUserRepository>();
        users.Setup(x => x.GetUserByIdWithBranchAsync(It.IsAny<Guid>())).ReturnsAsync((SmashCourt_BE.Models.Entities.User?)null);
        var service = new UserManagementService(users.Object, Mock.Of<IUserBranchRepository>(), Mock.Of<IBranchRepository>(), Mock.Of<IRefreshTokenRepository>(), new EmailService(TestConfigurationFactory.Create()), Mock.Of<ILogger<UserManagementService>>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetUserByIdAsync(Guid.NewGuid(), Guid.NewGuid(), "BRANCH_MANAGER"));

        Assert.Equal(ErrorCodes.UserNotFound, exception.ErrorCode);
    }
}
