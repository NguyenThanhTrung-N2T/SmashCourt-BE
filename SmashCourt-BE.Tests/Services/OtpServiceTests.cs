using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

[Trait("Category", TestCategories.Otp)]
[Trait("Category", TestCategories.Security)]
public class OtpServiceTests
{
    [Fact]
    public void GenerateCode_WhenGenerated_ReturnsSixDigits()
    {
        var service = new OtpService(TestConfigurationFactory.Create());

        var code = service.GenerateCode();

        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void GenerateCode_WhenCalledMultipleTimes_GeneratesDifferentCodes()
    {
        var service = new OtpService(TestConfigurationFactory.Create());

        var code1 = service.GenerateCode();
        var code2 = service.GenerateCode();

        // There's a tiny chance they could be equal, but very unlikely with 1 million possible values
        Assert.NotEqual(code1, code2);
    }

    [Fact]
    public void HashCode_WhenSameCode_ReturnsSameHash()
    {
        var service = new OtpService(TestConfigurationFactory.Create());

        var hash1 = service.HashCode(TestConstants.ValidOtpCode);
        var hash2 = service.HashCode(TestConstants.ValidOtpCode);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashCode_WhenDifferentCodes_ReturnsDifferentHashes()
    {
        var service = new OtpService(TestConfigurationFactory.Create());

        var hash1 = service.HashCode(TestConstants.ValidOtpCode);
        var hash2 = service.HashCode(TestConstants.InvalidOtpCode);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyCode_WhenCodeMatches_ReturnsTrue()
    {
        var service = new OtpService(TestConfigurationFactory.Create());
        var hash = service.HashCode(TestConstants.ValidOtpCode);

        var result = service.VerifyCode(TestConstants.ValidOtpCode, hash);

        Assert.True(result);
    }

    [Fact]
    public void VerifyCode_WhenCodeDoesNotMatch_ReturnsFalse()
    {
        var service = new OtpService(TestConfigurationFactory.Create());
        var hash = service.HashCode(TestConstants.ValidOtpCode);

        var result = service.VerifyCode(TestConstants.InvalidOtpCode, hash);

        Assert.False(result);
    }

    [Fact]
    public void HashRefreshToken_WhenSameToken_ReturnsSameHash()
    {
        var service = new OtpService(TestConfigurationFactory.Create());
        var token = "refresh-token-example";

        var hash1 = service.HashRefreshToken(token);
        var hash2 = service.HashRefreshToken(token);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashRefreshToken_WhenDifferentTokens_ReturnsDifferentHashes()
    {
        var service = new OtpService(TestConfigurationFactory.Create());

        var hash1 = service.HashRefreshToken("refresh-token-1");
        var hash2 = service.HashRefreshToken("refresh-token-2");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashRefreshToken_WhenTokenIsEmpty_ReturnsHash()
    {
        var service = new OtpService(TestConfigurationFactory.Create());

        var hash = service.HashRefreshToken(string.Empty);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }
}
