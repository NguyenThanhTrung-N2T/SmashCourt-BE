using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
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

    [Fact]
    public async Task GetVersionDetailAsync_WhenVersionDoesNotExist_ThrowsNotFound()
    {
        var courtTypeId = Guid.NewGuid();
        var courtTypes = new Mock<ICourtTypeRepository>();
        var prices = new Mock<ISystemPriceRepository>();
        courtTypes.Setup(x => x.GetByIdAsync(courtTypeId))
            .ReturnsAsync(new CourtType { Id = courtTypeId, Name = "Standard" });
        prices.Setup(x => x.GetExactDatePricesAsync(courtTypeId, It.IsAny<DateOnly>()))
            .ReturnsAsync([]);

        var exception = await Assert.ThrowsAsync<AppException>(() => new SystemPriceService(
            prices.Object, Mock.Of<ITimeSlotRepository>(), courtTypes.Object)
            .GetVersionDetailAsync(courtTypeId, new DateOnly(2026, 1, 1)));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteVersionAsync_WhenEffectiveDateIsPast_ThrowsBadRequest()
    {
        var courtTypeId = Guid.NewGuid();
        var courtTypes = new Mock<ICourtTypeRepository>();
        courtTypes.Setup(x => x.GetByIdAsync(courtTypeId))
            .ReturnsAsync(new CourtType { Id = courtTypeId });

        var exception = await Assert.ThrowsAsync<AppException>(() => new SystemPriceService(
            Mock.Of<ISystemPriceRepository>(), Mock.Of<ITimeSlotRepository>(), courtTypes.Object)
            .DeleteVersionAsync(courtTypeId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));

        Assert.Equal(ErrorCodes.BadRequest, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteVersionAsync_WhenFutureVersionDoesNotExist_ThrowsNotFound()
    {
        var courtTypeId = Guid.NewGuid();
        var prices = new Mock<ISystemPriceRepository>();
        var courtTypes = new Mock<ICourtTypeRepository>();
        courtTypes.Setup(x => x.GetByIdAsync(courtTypeId))
            .ReturnsAsync(new CourtType { Id = courtTypeId });
        prices.Setup(x => x.DeleteVersionAsync(courtTypeId, It.IsAny<DateOnly>())).ReturnsAsync(0);

        var exception = await Assert.ThrowsAsync<AppException>(() => new SystemPriceService(
            prices.Object, Mock.Of<ITimeSlotRepository>(), courtTypes.Object)
            .DeleteVersionAsync(courtTypeId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }

    [Fact]
    public async Task GetEffectivePricesAsync_WhenNoPricesExist_ReturnsEmptyList()
    {
        var prices = new Mock<ISystemPriceRepository>();
        prices.Setup(x => x.GetCurrentForDateAsync(It.IsAny<DateOnly>(), null)).ReturnsAsync([]);

        var result = await new SystemPriceService(
            prices.Object, Mock.Of<ITimeSlotRepository>(), Mock.Of<ICourtTypeRepository>())
            .GetEffectivePricesAsync(new DateOnly(2026, 1, 1));

        Assert.Empty(result);
    }

    [Fact]
    public async Task UpsertVersionAsync_WhenEffectiveDateIsPast_ThrowsBadRequestBeforeLoadingSlots()
    {
        var courtTypeId = Guid.NewGuid();
        var courtTypes = new Mock<ICourtTypeRepository>();
        var timeSlots = new Mock<ITimeSlotRepository>();
        courtTypes.Setup(x => x.GetByIdAsync(courtTypeId))
            .ReturnsAsync(new CourtType { Id = courtTypeId });

        var exception = await Assert.ThrowsAsync<AppException>(() => new SystemPriceService(
            Mock.Of<ISystemPriceRepository>(), timeSlots.Object, courtTypes.Object)
            .UpsertVersionAsync(
                courtTypeId,
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                new UpsertPriceRequest()));

        Assert.Equal(ErrorCodes.BadRequest, exception.ErrorCode);
        timeSlots.Verify(x => x.GetAllAsync(), Times.Never);
    }
}
