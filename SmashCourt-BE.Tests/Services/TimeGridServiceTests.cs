using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class TimeGridServiceTests
{
    [Fact]
    public async Task GetTimeGridAsync_MissingCourtThrowsNotFound()
    {
        var courts = new Mock<ICourtRepository>();
        courts.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid?>())).ReturnsAsync((Court?)null);
        var service = new TimeGridService(Mock.Of<ITimeSlotRepository>(), Mock.Of<IBookingRepository>(), Mock.Of<ISlotLockRepository>(), courts.Object);

        var exception = await Assert.ThrowsAsync<AppException>(() => service.GetTimeGridAsync(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today)));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }
}
