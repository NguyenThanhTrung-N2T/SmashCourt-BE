using Microsoft.Extensions.Configuration;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Configurations;

/// <summary>
/// OPS-03 (Epic 6C, gói 6C-1 K4): image không còn chứa appsettings.Development.json, nên cấu hình bắt buộc phải được kiểm ngay khi khởi động.
/// Thiếu khóa cốt lõi (DB, JWT, OTP) ⇒ dừng khởi động, thông báo nêu tên khóa và không chứa giá trị; thiếu khóa của tính năng phụ ⇒ chỉ cảnh báo.
/// Giá trị mẫu của appsettings.json (<c>your-…</c>, <c>add-…</c>) tính là thiếu.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "OPS-03")]
public class RequiredConfigurationTests
{
    private const string SecretValue = "gia-tri-bi-mat-khong-duoc-in-ra-1234567890";

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> AllCoreKeys() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=x;Username=u;Password=" + SecretValue,
        ["Jwt:Key"] = SecretValue,
        ["Jwt:Issuer"] = "smashcourt",
        ["Jwt:Audience"] = "smashcourt-fe",
        ["Otp:HmacSecret"] = SecretValue,
    };

    [Fact]
    public void BlockingKeys_AreExactlyDatabaseJwtAndOtp()
    {
        Assert.Equal(
            new[] { "ConnectionStrings:DefaultConnection", "Jwt:Key", "Jwt:Issuer", "Jwt:Audience", "Otp:HmacSecret" },
            RequiredConfiguration.BlockingKeys);
    }

    [Fact]
    public void EnsureBlockingKeys_WhenAllCoreKeysSet_DoesNotThrow()
    {
        var exception = Record.Exception(() => RequiredConfiguration.EnsureBlockingKeys(Config(AllCoreKeys())));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("Jwt:Key")]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    [InlineData("Otp:HmacSecret")]
    public void EnsureBlockingKeys_WhenOneCoreKeyMissing_ThrowsNamingTheKeyWithoutValues(string key)
    {
        var values = AllCoreKeys();
        values.Remove(key);

        var exception = Assert.Throws<InvalidOperationException>(() => RequiredConfiguration.EnsureBlockingKeys(Config(values)));

        Assert.Contains(key, exception.Message);
        Assert.DoesNotContain(SecretValue, exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("your-secret-key")]
    [InlineData("your-otp-hmac-secret")]
    [InlineData("add-key-here")]
    public void EnsureBlockingKeys_WhenCoreKeyIsEmptyOrTemplatePlaceholder_Throws(string value)
    {
        var values = AllCoreKeys();
        values["Jwt:Key"] = value;

        var exception = Assert.Throws<InvalidOperationException>(() => RequiredConfiguration.EnsureBlockingKeys(Config(values)));

        Assert.Contains("Jwt:Key", exception.Message);
    }

    [Fact]
    public void EnsureBlockingKeys_WhenSeveralMissing_ListsEveryMissingKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RequiredConfiguration.EnsureBlockingKeys(Config(new())));

        foreach (var key in RequiredConfiguration.BlockingKeys)
            Assert.Contains(key, exception.Message);
    }

    [Fact]
    public void MissingOptionalKeys_ReturnsOnlyUnsetOrPlaceholderFeatureKeys()
    {
        var values = AllCoreKeys();
        values["Smtp:Username"] = "mailer@example.com";
        values["Smtp:Password"] = SecretValue;
        values["Cloudinary:CloudName"] = "your-cloud-name";

        var missing = RequiredConfiguration.MissingOptionalKeys(Config(values));

        Assert.DoesNotContain("Smtp:Username", missing);
        Assert.DoesNotContain("Smtp:Password", missing);
        Assert.Contains("Cloudinary:CloudName", missing);
        Assert.Contains("VnPay:HashSecret", missing);
        Assert.All(missing, key => Assert.Contains(key, RequiredConfiguration.OptionalKeys));
    }

    [Fact]
    public void OptionalKeys_CoverEveryFeatureSecretButNoCoreKey()
    {
        Assert.Equal(
            new[]
            {
                "Smtp:Username", "Smtp:Password",
                "VnPay:TmnCode", "VnPay:HashSecret", "VnPay:CallbackUrl",
                "Cloudinary:CloudName", "Cloudinary:ApiKey", "Cloudinary:ApiSecret",
                "Google:ClientId", "Google:ClientSecret",
            },
            RequiredConfiguration.OptionalKeys);
        Assert.Empty(RequiredConfiguration.OptionalKeys.Intersect(RequiredConfiguration.BlockingKeys));
    }
}
