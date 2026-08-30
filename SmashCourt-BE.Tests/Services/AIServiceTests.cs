using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Integrations.AI;
using SmashCourt_BE.Integrations.AI.DTOs;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.Services;

public class AIServiceTests
{
    [Fact]
    public async Task ProcessChatAsync_NullAiResponseReturnsFallback()
    {
        var client = new Mock<IFastApiClient>();
        var formatter = new Mock<IAIResponseFormatterService>();
        var expected = new ChatResponseDto { Reply = "Fallback" };
        formatter.Setup(x => x.GetFallbackChatResponse()).Returns(expected);
        var preparation = new AIDataPreparationService(Mock.Of<ILogger<AIDataPreparationService>>(), Mock.Of<IReportService>(), Mock.Of<IBookingRepository>(), Mock.Of<IUserBranchRepository>());
        var service = new AIService(client.Object, preparation, formatter.Object, Mock.Of<IUserBranchRepository>(), Mock.Of<IBranchRepository>(), Mock.Of<ILogger<AIService>>());

        var result = await service.ProcessChatAsync(new ChatRequestDto { Message = "How do I book?" });

        Assert.Same(expected, result);
        formatter.Verify(x => x.GetFallbackChatResponse(), Times.Once);
    }
}
