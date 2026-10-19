using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class BranchStaffServiceTests
{
    [Fact]
    public async Task GetStaffAsync_MissingBranchThrowsBranchNotFound()
    {
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Branch?)null);
        var service = new BranchStaffService(Mock.Of<IUserBranchRepository>(), Mock.Of<IUserRepository>(), branches.Object);

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetStaffAsync(Guid.NewGuid(), new SmashCourt_BE.DTOs.BranchManagement.StaffFilterQuery()));

        Assert.Equal(ErrorCodes.BranchNotFound, exception.ErrorCode);
    }
}
