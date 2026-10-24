using Microsoft.Extensions.Configuration;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Configurations;

/// <summary>Cấu hình rate limit (B1/D9): mặc định đúng giá trị cũ của Program.cs, đọc được từ cấu hình, kiểm khi khởi động.</summary>
[Trait("Category", TestCategories.Configuration)]
public class RateLimitSettingsTests
{
    private static readonly string[] PolicyProperties =
        [nameof(RateLimitSettings.Login), nameof(RateLimitSettings.Booking), nameof(RateLimitSettings.Sensitive),
         nameof(RateLimitSettings.AiPublic), nameof(RateLimitSettings.AiUser), nameof(RateLimitSettings.AiManagement)];

    private static RateLimitPolicySettings Get(RateLimitSettings settings, string property) =>
        (RateLimitPolicySettings)typeof(RateLimitSettings).GetProperty(property)!.GetValue(settings)!;

    [Theory]
    [InlineData("Login", 5)]
    [InlineData("Booking", 20)]
    [InlineData("Sensitive", 10)]
    [InlineData("AiPublic", 100)]
    [InlineData("AiUser", 20)]
    [InlineData("AiManagement", 10)]
    public void Defaults_EqualTheValuesThatWereHardCodedInProgram(string property, int permitLimit)
    {
        var policy = Get(new RateLimitSettings(), property);

        Assert.Equal(permitLimit, policy.PermitLimit);
        Assert.Equal(60, policy.WindowSeconds);
    }

    [Theory]
    [InlineData("login", "Login")]
    [InlineData("booking", "Booking")]
    [InlineData("sensitive", "Sensitive")]
    [InlineData("ai-public", "AiPublic")]
    [InlineData("ai-user", "AiUser")]
    [InlineData("ai-management", "AiManagement")]
    public void ForPolicy_MapsEachAttributeNameToItsSettings(string policyName, string property)
    {
        var settings = new RateLimitSettings();

        Assert.Same(Get(settings, property), settings.ForPolicy(policyName));
    }

    [Fact]
    public void ForPolicy_WhenNameIsUnknown_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimitSettings().ForPolicy("nope"));
    }

    [Fact]
    public void PolicyNames_ListsExactlyTheSixPoliciesUsedByControllers()
    {
        Assert.Equal(
            new[] { "ai-management", "ai-public", "ai-user", "booking", "login", "sensitive" },
            RateLimitSettings.PolicyNames.OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Bind_ReadsOnlyTheOverriddenPolicyAndKeepsOthersAtDefault()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimiting:Booking:PermitLimit"] = "1000",
            ["RateLimiting:AiPublic:WindowSeconds"] = "5"
        }).Build();

        var settings = config.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>()!;

        Assert.Equal(1000, settings.Booking.PermitLimit);
        Assert.Equal(60, settings.Booking.WindowSeconds);
        Assert.Equal(100, settings.AiPublic.PermitLimit);
        Assert.Equal(5, settings.AiPublic.WindowSeconds);
        Assert.Equal(5, settings.Login.PermitLimit);
    }

    // ───────────────────────── Validator ─────────────────────────

    private readonly RateLimitSettingsValidator _validator = new();

    [Fact]
    public void Validate_WhenDefaults_IsValid()
    {
        Assert.True(_validator.Validate(null, new RateLimitSettings()).Succeeded);
    }

    public static TheoryData<string, int> PolicyAndBadValue()
    {
        var data = new TheoryData<string, int>();
        foreach (var property in PolicyProperties)
            foreach (var bad in new[] { 0, -1 })
                data.Add(property, bad);
        return data;
    }

    [Theory]
    [MemberData(nameof(PolicyAndBadValue))]
    public void Validate_WhenPermitLimitIsNotPositive_FailsNamingThePolicy(string property, int bad)
    {
        var settings = new RateLimitSettings();
        Get(settings, property).PermitLimit = bad;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains($"{RateLimitSettings.SectionName}:{property}:PermitLimit", result.FailureMessage);
    }

    [Theory]
    [MemberData(nameof(PolicyAndBadValue))]
    public void Validate_WhenWindowIsNotPositive_FailsNamingThePolicy(string property, int bad)
    {
        var settings = new RateLimitSettings();
        Get(settings, property).WindowSeconds = bad;

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains($"{RateLimitSettings.SectionName}:{property}:WindowSeconds", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenSeveralPoliciesAreWrong_ReportsEveryOne()
    {
        var settings = new RateLimitSettings();
        settings.Login.PermitLimit = 0;
        settings.Booking.WindowSeconds = 0;
        settings.AiUser.PermitLimit = -5;

        var result = _validator.Validate(null, settings);

        Assert.Contains("Login:PermitLimit", result.FailureMessage);
        Assert.Contains("Booking:WindowSeconds", result.FailureMessage);
        Assert.Contains("AiUser:PermitLimit", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenAPolicyIsNull_Fails()
    {
        var settings = new RateLimitSettings { Login = null! };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("Login", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenValuesAreVeryLarge_IsValid()
    {
        var settings = new RateLimitSettings();
        settings.Booking.PermitLimit = int.MaxValue;
        settings.Booking.WindowSeconds = 86_400;

        Assert.True(_validator.Validate(null, settings).Succeeded);
    }
}
