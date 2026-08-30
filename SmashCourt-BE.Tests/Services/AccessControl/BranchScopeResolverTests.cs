using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.AccessControl;

namespace SmashCourt_BE.Tests.Services.AccessControl;

public class BranchScopeResolverTests
{
    [Fact]
    public async Task ResolveRequiredBranchIdAsync_OwnerMustProvideBranch()
    {
        var resolver = new BranchScopeResolver(Mock.Of<IUserBranchRepository>(), Mock.Of<IBranchRepository>());

        var exception = await Assert.ThrowsAsync<AppException>(() => resolver.ResolveRequiredBranchIdAsync(null, Guid.NewGuid(), UserRole.OWNER));

        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveRequiredBranchIdAsync_StaffWithoutAssignmentIsForbidden()
    {
        var assignments = new Mock<IUserBranchRepository>();
        assignments.Setup(x => x.GetActiveByUserIdAsync(It.IsAny<Guid>())).ReturnsAsync((UserBranch?)null);
        var resolver = new BranchScopeResolver(assignments.Object, Mock.Of<IBranchRepository>());

        var exception = await Assert.ThrowsAsync<AppException>(() => resolver.ResolveRequiredBranchIdAsync(null, Guid.NewGuid(), UserRole.STAFF));

        Assert.Equal(ErrorCodes.Forbidden, exception.ErrorCode);
    }

    [Fact]
    public async Task ResolveOptionalBranchIdAsync_OwnerMayRequestAllBranches()
    {
        var resolver = new BranchScopeResolver(Mock.Of<IUserBranchRepository>(), Mock.Of<IBranchRepository>());

        var result = await resolver.ResolveOptionalBranchIdAsync(null, Guid.NewGuid(), UserRole.OWNER);

        Assert.Null(result);
    }
}
