using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Court;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.AccessControl;

namespace SmashCourt_BE.Tests.Services;

public class CourtServiceTests
{
    [Fact]
    public async Task GetAllAsync_WhenPublicRequestOmitsBranch_ThrowsBadRequest()
    {
        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().GetAllAsync(
            null, null, null, "CUSTOMER"));

        Assert.Equal(ErrorCodes.BadRequest, exception.ErrorCode);
    }

    [Fact]
    public async Task GetAllAsync_WhenBranchDoesNotExist_ThrowsNotFound()
    {
        var branchId = Guid.NewGuid();
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.GetByIdAsync(branchId)).ReturnsAsync((SmashCourt_BE.Models.Entities.Branch?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService(branches: branches)
            .GetAllAsync(branchId, null, null, "CUSTOMER"));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCourtDoesNotExist_ThrowsNotFound()
    {
        var courts = new Mock<ICourtRepository>();
        courts.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), null))
            .ReturnsAsync((SmashCourt_BE.Models.Entities.Court?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService(courts: courts)
            .GetByIdAsync(Guid.NewGuid(), null, null, null));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_WhenRoleIsInvalid_ThrowsForbidden()
    {
        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().CreateAsync(
            Guid.NewGuid(), new CreateCourtDto(), Guid.NewGuid(), "INVALID"));

        Assert.Equal(ErrorCodes.Forbidden, exception.ErrorCode);
    }

    [Fact]
    public async Task GetAllAsync_WhenCustomerRequestsInactiveBranch_ThrowsNotFound()
    {
        var branchId = Guid.NewGuid();
        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.GetByIdAsync(branchId))
            .ReturnsAsync(new SmashCourt_BE.Models.Entities.Branch
            {
                Id = branchId,
                Status = SmashCourt_BE.Models.Enums.BranchStatus.INACTIVE
            });

        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService(branches: branches)
            .GetAllAsync(branchId, null, null, "CUSTOMER"));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    private static CourtService CreateService(
        Mock<ICourtRepository>? courts = null,
        Mock<IBranchRepository>? branches = null) =>
        new(
            (courts ?? new Mock<ICourtRepository>()).Object,
            (branches ?? new Mock<IBranchRepository>()).Object,
            Mock.Of<IUserBranchRepository>(),
            Mock.Of<ICourtTypeRepository>(),
            Mock.Of<IBranchPriceRepository>(),
            Mock.Of<ISystemPriceRepository>(),
            Mock.Of<ITimeSlotRepository>(),
            Mock.Of<IBranchScopeResolver>());
}
