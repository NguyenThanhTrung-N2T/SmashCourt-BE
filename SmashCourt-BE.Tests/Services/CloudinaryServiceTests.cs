using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Services;

public class CloudinaryServiceTests
{
    [Fact]
    public async Task UploadImageAsync_NullFileThrowsValidationError()
    {
        var service = new CloudinaryService(Options.Create(new CloudinarySettings
        {
            CloudName = "cloud",
            ApiKey = "key",
            ApiSecret = "secret"
        }));

        var exception = await Assert.ThrowsAsync<AppException>(() => service.UploadImageAsync(null!));

        Assert.Equal(400, exception.StatusCode);
    }
}
