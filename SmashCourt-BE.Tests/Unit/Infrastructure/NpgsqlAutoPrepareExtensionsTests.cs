using Microsoft.Extensions.Configuration;
using Npgsql;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Infrastructure;

/// <summary>
/// P6-54 (plan 6C, 05/11): auto-prepare của Npgsql bật theo cấu hình <c>Database:MaxAutoPrepare</c>, mặc định TẮT (0) để môi trường chưa kiểm
/// (Neon qua pooler) giữ hành vi cũ; chuỗi kết nối ghi rõ <c>Max Auto Prepare</c> thì chuỗi thắng; giá trị sai dừng lúc khởi động.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "OPS-06")]
public class NpgsqlAutoPrepareExtensionsTests
{
    private const string ConnectionString = "Host=127.0.0.1;Database=smashcourt;Username=u;Password=p";

    private static IConfiguration Config(string? value) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [NpgsqlAutoPrepareExtensions.ConfigKey] = value })
        .Build();

    private static int Apply(string connectionString, IConfiguration configuration) =>
        new NpgsqlDataSourceBuilder(connectionString).UseSmashCourtAutoPrepare(configuration).ConnectionStringBuilder.MaxAutoPrepare;

    [Fact]
    public void UseSmashCourtAutoPrepare_WhenNotConfigured_StaysOff()
    {
        Assert.Equal(0, Apply(ConnectionString, new ConfigurationBuilder().Build()));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50", 50)]
    [InlineData("256", 256)]
    public void UseSmashCourtAutoPrepare_WhenConfigured_UsesConfiguredValue(string value, int expected)
    {
        Assert.Equal(expected, Apply(ConnectionString, Config(value)));
    }

    [Theory]
    [InlineData("Max Auto Prepare=0", "50", 0)]
    [InlineData("Max Auto Prepare=20", null, 20)]
    [InlineData("MaxAutoPrepare=30", "50", 30)]
    public void UseSmashCourtAutoPrepare_WhenConnectionStringSetsIt_ConnectionStringWins(string key, string? configured, int expected)
    {
        Assert.Equal(expected, Apply($"{ConnectionString};{key}", Config(configured)));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("257")]
    [InlineData("abc")]
    public void UseSmashCourtAutoPrepare_WhenConfiguredValueInvalid_ThrowsNamingTheKey(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Apply(ConnectionString, Config(value)));
        Assert.Contains(NpgsqlAutoPrepareExtensions.ConfigKey, error.Message);
    }

    // Người dùng chốt 05/11: auto-prepare chỉ cho kiểm thử và đánh giá (perf); bản deploy và cấu hình chung không bật
    [Theory]
    [InlineData("deploy/docker-compose.yml")]
    [InlineData("appsettings.json")]
    public void DeployConfiguration_DoesNotEnableAutoPrepare(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        Assert.DoesNotContain("MaxAutoPrepare", text);
        Assert.DoesNotContain("Auto Prepare", text);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SmashCourt-BE.csproj")))
                return dir.FullName;
        throw new InvalidOperationException("Không tìm thấy thư mục SmashCourt-BE (chứa SmashCourt-BE.csproj).");
    }

    [Fact]
    public void UseSmashCourtAutoPrepare_DoesNotChangeOtherConnectionSettings()
    {
        var builder = new NpgsqlDataSourceBuilder($"{ConnectionString};Maximum Pool Size=37").UseSmashCourtAutoPrepare(Config("50"));
        Assert.Equal(37, builder.ConnectionStringBuilder.MaxPoolSize);
        Assert.Equal("smashcourt", builder.ConnectionStringBuilder.Database);
        Assert.Equal(5, builder.ConnectionStringBuilder.AutoPrepareMinUsages);
    }
}
