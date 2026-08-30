using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

public class GoogleAuthServiceTests
{
    [Fact]
    public void GenerateAuthUrl_ContainsConfiguredOAuthParameters()
    {
        var settings = Options.Create(new GoogleSettings
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://localhost/callback"
        });
        var service = new GoogleAuthService(settings, new MemoryCache(new MemoryCacheOptions()), Mock.Of<IUserRepository>(), Mock.Of<IOAuthAccountRepository>(), Mock.Of<IRefreshTokenRepository>(), Mock.Of<SmashCourt_BE.Services.IService.ITokenService>(), new OtpService(TestConfigurationFactory.Create()), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<GoogleAuthService>>(), Mock.Of<ICustomerLoyaltyRepository>(), Mock.Of<ILoyaltyTierRepository>(), new HttpContextAccessor());

        var url = service.GenerateAuthUrl();

        Assert.Contains("client_id=client-id", url);
        Assert.Contains("redirect_uri=", url);
        Assert.Contains("state=", url);
    }
}
