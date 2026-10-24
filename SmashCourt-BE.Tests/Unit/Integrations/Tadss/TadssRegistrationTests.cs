using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>Đăng ký DI của TADSS: client resolve được từ container, cấu hình được kiểm, không theo redirect.</summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "BE-UT-01")]
public class TadssRegistrationTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddTadssIntegration(config);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void AddTadssIntegration_WhenConfigIsValid_ResolvesClientAndBindsSettings()
    {
        using var provider = BuildProvider(new()
        {
            ["TadssConfig:EnableTadssDecision"] = "true",
            ["TadssConfig:BaseUrl"] = "https://tadss.example",
            ["TadssConfig:ApiKey"] = "dev-key",
            ["TadssConfig:TimeoutMs"] = "500"
        });
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ITadssClient>();
        var settings = provider.GetRequiredService<IOptionsMonitor<TadssSettings>>().CurrentValue;

        Assert.IsType<TadssClient>(client);
        Assert.True(settings.EnableTadssDecision);
        Assert.Equal("https://tadss.example", settings.BaseUrl);
        Assert.Equal(500, settings.TimeoutMs);
        Assert.Equal(15, settings.ApprovedPaymentWindowMinutes); // mặc định khi không cấu hình
    }

    [Fact]
    public void AddTadssIntegration_WhenFlagOnWithoutApiKey_SettingsAccessThrowsValidationError()
    {
        using var provider = BuildProvider(new() { ["TadssConfig:EnableTadssDecision"] = "true" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptionsMonitor<TadssSettings>>().CurrentValue);

        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void AddTadssIntegration_WhenNothingConfigured_DefaultsAreValidWithFlagOff()
    {
        using var provider = BuildProvider(new());

        var settings = provider.GetRequiredService<IOptionsMonitor<TadssSettings>>().CurrentValue;

        Assert.False(settings.EnableTadssDecision);
        Assert.Equal(800, settings.TimeoutMs);
    }

    [Fact]
    public void AddTadssIntegration_WhenHandlerChainIsBuilt_DoesNotFollowRedirects()
    {
        using var provider = BuildProvider(new());
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var handler = factory.CreateHandler(nameof(ITadssClient));
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        var primary = Assert.IsType<HttpClientHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
    }
}
