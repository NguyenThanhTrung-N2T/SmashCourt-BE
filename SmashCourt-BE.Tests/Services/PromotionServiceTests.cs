using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class PromotionServiceTests
{
    [Fact]
    public async Task GetByIdAsync_MissingPromotionThrowsNotFound()
    {
        var repository = new Mock<IPromotionRepository>();
        repository.Setup(x => x.GetByIdWithConditionsAsync(It.IsAny<Guid>())).ReturnsAsync((Promotion?)null);
        var engine = new PromotionEngineService(repository.Object);

        var exception = await Assert.ThrowsAsync<AppException>(() => new PromotionService(repository.Object, engine, Mock.Of<IBookingRepository>()).GetByIdAsync(Guid.NewGuid()));

        Assert.Equal(ErrorCodes.NotFound, exception.ErrorCode);
    }
}
