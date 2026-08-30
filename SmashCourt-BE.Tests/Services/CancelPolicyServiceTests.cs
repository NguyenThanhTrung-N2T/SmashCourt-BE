using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.CancelPolicy;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class CancelPolicyServiceTests
{
    [Fact]
    public async Task CreatePolicyAsync_DuplicateHoursThrowsConflict()
    {
        var repository = new Mock<ICancelPolicyRepository>();
        repository.Setup(x => x.GetByHoursBeforeAsync(24)).ReturnsAsync(new CancelPolicy());

        var exception = await Assert.ThrowsAsync<AppException>(() => new CancelPolicyService(repository.Object).CreatePolicyAsync(
            new CreateCancelPolicyDto { HoursBefore = 24, RefundPercent = 50 }));

        Assert.Equal(ErrorCodes.Conflict, exception.ErrorCode);
    }

    [Fact]
    public async Task DeletePolicyAsync_CannotDeleteLastPolicy()
    {
        var repository = new Mock<ICancelPolicyRepository>();
        var policy = new CancelPolicy { Id = Guid.NewGuid() };
        repository.Setup(x => x.GetByIdAsync(policy.Id)).ReturnsAsync(policy);
        repository.Setup(x => x.CountAsync()).ReturnsAsync(1);

        var exception = await Assert.ThrowsAsync<AppException>(() => new CancelPolicyService(repository.Object).DeletePolicyAsync(policy.Id));

        Assert.Equal(422, exception.StatusCode);
        repository.Verify(x => x.DeleteAsync(It.IsAny<CancelPolicy>()), Times.Never);
    }
}
