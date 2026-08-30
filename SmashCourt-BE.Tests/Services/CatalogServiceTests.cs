using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Service;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.AccessControl;

namespace SmashCourt_BE.Tests.Services;

public class CatalogServiceTests
{
    [Fact]
    public async Task BranchService_GetByIdAsync_MissingBranchThrowsNotFound()
    {
        var repository = new Mock<IBranchRepository>();
        repository.Setup(x => x.GetWithManagerAsync(It.IsAny<Guid>())).ReturnsAsync(((Branch, UserBranch?)?)null);
        var service = new SmashCourt_BE.Services.BranchService(repository.Object, Mock.Of<IUserRepository>(), Mock.Of<IUserBranchRepository>(), Mock.Of<ILogger<SmashCourt_BE.Services.BranchService>>(), Mock.Of<IServiceRepository>(), Mock.Of<IBranchScopeResolver>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetByIdAsync(Guid.NewGuid(), false));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task CourtTypeService_GetByIdAsync_MissingTypeThrowsNotFound()
    {
        var repository = new Mock<ICourtTypeRepository>();
        repository.Setup(x => x.GetWithCountByIdAsync(It.IsAny<Guid>())).ReturnsAsync((CourtTypeWithCount?)null);
        var service = new CourtTypeService(repository.Object, Mock.Of<IBranchRepository>(), Mock.Of<ICourtRepository>(), Mock.Of<IUserBranchRepository>(), Mock.Of<IBranchScopeResolver>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetByIdAsync(Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task ServiceService_CreateAsync_DuplicateNameThrowsConflict()
    {
        var repository = new Mock<IServiceRepository>();
        repository.Setup(x => x.ExistsByNameAsync("Ball", null)).ReturnsAsync(true);
        var service = new ServiceService(repository.Object);

        var exception = await Assert.ThrowsAsync<AppException>(() => service.CreateAsync(new CreateServiceDto
        {
            Name = "Ball",
            Unit = "piece",
            DefaultPrice = 10
        }));

        Assert.Equal(ErrorCodes.NameDuplicate, exception.ErrorCode);
    }

    [Fact]
    public async Task BranchPriceService_InvalidRoleIsForbidden()
    {
        var service = new BranchPriceService(Mock.Of<IBranchPriceRepository>(), Mock.Of<ISystemPriceRepository>(), Mock.Of<ITimeSlotRepository>(), Mock.Of<IBranchRepository>(), Mock.Of<ICourtRepository>(), Mock.Of<IBranchScopeResolver>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetEffectivePricesAsync(null, DateOnly.FromDateTime(DateTime.Today), null, Guid.NewGuid(), "INVALID"));

        Assert.Equal(ErrorCodes.Forbidden, exception.ErrorCode);
    }

    [Fact]
    public async Task CourtService_GetByIdAsync_MissingCourtThrowsNotFound()
    {
        var repository = new Mock<ICourtRepository>();
        repository.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid?>())).ReturnsAsync((Court?)null);
        var service = new CourtService(repository.Object, Mock.Of<IBranchRepository>(), Mock.Of<IUserBranchRepository>(), Mock.Of<ICourtTypeRepository>(), Mock.Of<IBranchPriceRepository>(), Mock.Of<ISystemPriceRepository>(), Mock.Of<ITimeSlotRepository>(), Mock.Of<IBranchScopeResolver>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetByIdAsync(Guid.NewGuid(), null, null, null));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }
}
