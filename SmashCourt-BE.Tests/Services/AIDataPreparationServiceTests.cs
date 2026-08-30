using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.Services;

public class AIDataPreparationServiceTests
{
    [Fact]
    public async Task BuildPublicChatContextAsync_ExcludesUserSpecificData()
    {
        var service = new AIDataPreparationService(Mock.Of<ILogger<AIDataPreparationService>>(), Mock.Of<IReportService>(), Mock.Of<IBookingRepository>(), Mock.Of<IUserBranchRepository>());

        var context = await service.BuildPublicChatContextAsync();

        Assert.Contains("Booking Process", context);
        Assert.DoesNotContain("Completed Bookings", context);
        Assert.DoesNotContain("userId", context, StringComparison.OrdinalIgnoreCase);
    }
}
