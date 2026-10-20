using SmashCourt_BE.Configurations;
using SmashCourt_BE.Tests.Helpers;

namespace SmashCourt_BE.Tests.Configurations;

[Trait("Category", TestCategories.Configuration)]
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-01")]
public class TadssSettingsValidatorTests
{
    private readonly TadssSettingsValidator _validator = new();

    private static TadssSettings Enabled() => new()
    {
        EnableTadssDecision = true,
        BaseUrl = "http://localhost:5296",
        ApiKey = "dev-key"
    };

    [Fact]
    public void Validate_WhenDefaultsWithFlagOff_IsValidEvenWithoutApiKey()
    {
        var result = _validator.Validate(null, new TadssSettings());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenFlagOnWithKeyAndAbsoluteUrl_IsValid()
    {
        var result = _validator.Validate(null, Enabled());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenFlagOnWithoutApiKey_FailsMentioningApiKey()
    {
        var settings = Enabled();
        settings.ApiKey = "  ";

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("ApiKey", result.FailureMessage);
    }

    [Theory]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData("ftp://tadss.local")]
    public void Validate_WhenFlagOnWithBadBaseUrl_FailsMentioningBaseUrl(string baseUrl)
    {
        var settings = Enabled();
        settings.BaseUrl = baseUrl;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("BaseUrl", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenFlagOffWithBadBaseUrl_IsValid()
    {
        var settings = new TadssSettings { BaseUrl = "chưa cấu hình" };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(5001)]
    [InlineData(0)]
    public void Validate_WhenTimeoutOutOfRange_Fails(int timeoutMs)
    {
        var result = _validator.Validate(null, new TadssSettings { TimeoutMs = timeoutMs });

        Assert.True(result.Failed);
        Assert.Contains("TimeoutMs", result.FailureMessage);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(5000)]
    public void Validate_WhenTimeoutAtBoundary_IsValid(int timeoutMs)
    {
        var result = _validator.Validate(null, new TadssSettings { TimeoutMs = timeoutMs });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Validate_WhenApprovedPaymentWindowOutOfRange_Fails(int minutes)
    {
        var result = _validator.Validate(null, new TadssSettings { ApprovedPaymentWindowMinutes = minutes });

        Assert.True(result.Failed);
        Assert.Contains("ApprovedPaymentWindowMinutes", result.FailureMessage);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("SE Asia Standard Time")] // mã Windows: máy tìm được nhưng TADSS từ chối (400 VALIDATION_ERROR ở mọi lần gọi)
    [InlineData("")]
    public void Validate_WhenTimeZoneNotFound_Fails(string zone)
    {
        var result = _validator.Validate(null, new TadssSettings { BranchTimeZone = zone });

        Assert.True(result.Failed);
        Assert.Contains("BranchTimeZone", result.FailureMessage);
    }

    [Theory]
    [InlineData("khóa-có-dấu")]
    [InlineData("key with space")]
    public void Validate_WhenFlagOnWithApiKeyThatIsNotVisibleAscii_FailsMentioningApiKey(string apiKey)
    {
        var settings = Enabled();
        settings.ApiKey = apiKey;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("ApiKey", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenFlagOnWithApiKeyThatHasControlCharacters_FailsMentioningApiKey()
    {
        // Ký tự điều khiển dễ lọt vào khi chép khóa vào User Secrets: tab, xuống dòng
        foreach (var suffix in new[] { (char)9, (char)10, (char)13 })
        {
            var settings = Enabled();
            settings.ApiKey = "dev-key" + suffix;

            var result = _validator.Validate(null, settings);

            Assert.True(result.Failed);
            Assert.Contains("ApiKey", result.FailureMessage);
        }
    }

    [Fact]
    public void Validate_WhenSeveralRulesBroken_ReportsAllOfThem()
    {
        var settings = Enabled();
        settings.ApiKey = "";
        settings.TimeoutMs = 10;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("ApiKey", result.FailureMessage);
        Assert.Contains("TimeoutMs", result.FailureMessage);
    }
}
