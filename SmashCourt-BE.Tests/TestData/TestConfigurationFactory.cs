using Microsoft.Extensions.Configuration;

namespace SmashCourt_BE.Tests.TestData;

internal static class TestConfigurationFactory
{
    public static IConfiguration Create(IReadOnlyDictionary<string, string?>? values = null)
    {
        var data = new Dictionary<string, string?>
        {
            ["Otp:HmacSecret"] = TestConstants.TestSecret,
            ["Jwt:Key"] = TestConstants.JwtKey,
            ["Jwt:Issuer"] = TestConstants.JwtIssuer,
            ["Jwt:Audience"] = TestConstants.JwtAudience,
            ["Jwt:AccessTokenExpirationMinutes"] = TestConstants.JwtAccessTokenExpirationMinutes.ToString()
        };

        if (values is not null)
        {
            foreach (var item in values)
                data[item.Key] = item.Value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }
}
