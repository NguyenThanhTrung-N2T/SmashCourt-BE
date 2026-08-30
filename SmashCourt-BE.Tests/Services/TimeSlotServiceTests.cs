using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class TimeSlotServiceTests
{
    [Fact]
    public async Task CreateAsync_StartAfterEndThrowsBadRequest()
    {
        var service = new TimeSlotService(Mock.Of<ITimeSlotRepository>());

        var exception = await Assert.ThrowsAsync<AppException>(() => service.CreateAsync(new CreateTimeSlotDto
        {
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(9, 0, 0)
        }));

        Assert.Equal(ErrorCodes.BadRequest, exception.ErrorCode);
    }

    [Fact]
    public async Task DeleteAsync_InUseSlotCannotBeDeleted()
    {
        var repository = new Mock<ITimeSlotRepository>();
        var slot = new TimeSlot { Id = Guid.NewGuid() };
        repository.Setup(x => x.GetByIdAsync(slot.Id)).ReturnsAsync(slot);
        repository.Setup(x => x.IsInUseAsync(slot.Id)).ReturnsAsync(true);

        var exception = await Assert.ThrowsAsync<AppException>(() => new TimeSlotService(repository.Object).DeleteAsync(slot.Id));

        Assert.Equal(ErrorCodes.ResourceInUse, exception.ErrorCode);
        repository.Verify(x => x.DeleteBothAsync(It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()), Times.Never);
    }
}
