using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.Internal;

namespace SmashCourt_BE.Tests.Services;

public class AIResponseFormatterServiceTests
{
    [Fact]
    public void FormatChatResponse_NullResponse_ReturnsFallback()
    {
        var service = new AIResponseFormatterService(Mock.Of<ILogger<AIResponseFormatterService>>());

        var result = service.FormatChatResponse(null!);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Reply));
    }

    [Fact]
    public void FormatChatResponse_MissingReply_ReturnsFallback()
    {
        var service = new AIResponseFormatterService(Mock.Of<ILogger<AIResponseFormatterService>>());

        var result = service.FormatChatResponse(new AiChatResponseDto { Reply = " " });

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Reply));
    }
}
