using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class SystemPriceServiceTests
{
    [Fact]
    public async Task GetVersionsAsync_MissingCourtTypeThrowsNotFound()
    {
        var courtTypes = new Mock<ICourtTypeRepository>();
        courtTypes.Setup(x => x.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((CourtType?)null);
        var service = new SystemPriceService(Mock.Of<ISystemPriceRepository>(), Mock.Of<ITimeSlotRepository>(), courtTypes.Object);

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetVersionsAsync(Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }
}
