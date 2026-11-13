using System.Text.Json.Nodes;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Configurations;

/// <summary>
/// P6-57: mạch ngắt mặc định tắt trong mã (<c>appsettings.json</c>), bật ở bản deploy và E-STAGING. P6-58: bản deploy tắt cache quyết định. Mặc định tắt để bộ test HTTP dùng chung
/// một host không bị một test làm TADSS lỗi mở mạch cho các test sau.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "BE-UT-14")]
public class TadssCircuitConfigurationTests
{
    [Fact]
    public void AppSettings_KeepsTheCircuitOffByDefault()
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "appsettings.json")))
                   ?? throw new InvalidOperationException("appsettings.json rỗng");

        Assert.False(json["TadssConfig"]?["CircuitEnabled"]?.GetValue<bool>() ?? true);
    }

    [Theory]
    [InlineData("deploy/docker-compose.yml", "TadssConfig__CircuitEnabled: \"true\"")]
    [InlineData("docker-compose.yml", "- TadssConfig__CircuitEnabled=true")]
    public void DeployAndStagingCompose_TurnTheCircuitOn(string relativePath, string expectedLine)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), relativePath)).Select(line => line.Trim());

        Assert.Contains(expectedLine, lines);
    }

    // P6-58: bản deploy tắt cache quyết định của hai strategy quy tắc bằng cấu hình (khóa cache chứa mã đơn nên không bao giờ trúng)
    [Theory]
    [InlineData("Tadss__Strategies__Default__CacheTtlMinutes: \"0\"")]
    [InlineData("Tadss__Strategies__MockAlternative__CacheTtlMinutes: \"0\"")]
    public void DeployCompose_TurnsOffTheDecisionCacheOfRuleStrategies(string expectedLine)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "deploy", "docker-compose.yml")).Select(line => line.Trim());

        Assert.Contains(expectedLine, lines);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SmashCourt-BE.csproj")))
                return dir.FullName;
        throw new InvalidOperationException("Không tìm thấy thư mục SmashCourt-BE (chứa SmashCourt-BE.csproj).");
    }
}
