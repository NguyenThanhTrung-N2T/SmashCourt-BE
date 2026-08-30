using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class CustomerManagementServiceTests
{
    [Fact]
    public async Task GetCustomerByIdAsync_MissingCustomerThrowsUserNotFound()
    {
        var customers = new Mock<ICustomerManagementRepository>();
        customers.Setup(x => x.GetCustomerByIdAsync(It.IsAny<Guid>())).ReturnsAsync((SmashCourt_BE.Models.Entities.User?)null);
        var service = new CustomerManagementService(customers.Object, Mock.Of<IUserRepository>(), Mock.Of<IUserBranchRepository>(), Mock.Of<IRefreshTokenRepository>(), Mock.Of<ILoyaltyTierRepository>(), Mock.Of<ILogger<CustomerManagementService>>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetCustomerByIdAsync(Guid.NewGuid(), Guid.NewGuid(), "OWNER"));

        Assert.Equal(ErrorCodes.UserNotFound, exception.ErrorCode);
    }
}
