using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Extensions;

/// <summary>
/// BE-IT-19 ở bản deploy (12/11/2026): cờ <c>EnableTadssDecision</c> đổi bằng file <c>runtime-config/feature-flags.json</c> khi đang chạy,
/// không khởi động lại BE. File là nguồn cấu hình cuối (thắng appsettings và biến môi trường), nạp lại trong 2 giây cả khi ghi đè lẫn khi
/// script thay file bằng mv; không có thư mục thì không đổi gì (dev, host test, CI). Compose deploy gắn volume và không ghim cờ bằng biến môi trường.
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "BE-IT-19")]
public sealed class RuntimeConfigExtensionsTests : IDisposable
{
    private static readonly TimeSpan ReloadBudget = TimeSpan.FromSeconds(2);
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "smashcourt-runtime-config-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];

    public RuntimeConfigExtensionsTests() => Directory.CreateDirectory(_contentRoot);

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        Directory.Delete(_contentRoot, recursive: true);
    }

    private string FlagDirectory => Path.Combine(_contentRoot, RuntimeConfigExtensions.DirectoryName);
    private string FlagFile => Path.Combine(FlagDirectory, RuntimeConfigExtensions.FileName);

    private static string FlagJson(bool enabled) =>
        $"{{ \"TadssConfig\": {{ \"EnableTadssDecision\": {(enabled ? "true" : "false")} }} }}";

    // Cấu hình như Program.cs: các nguồn có sẵn (ở đây là bộ nhớ, đặt cờ = earlier) rồi mới tới file cờ
    private IOptionsMonitor<TadssSettings> Monitor(bool earlier)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["TadssConfig:EnableTadssDecision"] = earlier ? "true" : "false" })
            .AddSmashCourtRuntimeOverrides(_contentRoot)
            .Build();
        _disposables.Add((IDisposable)configuration);
        var services = new ServiceCollection();
        services.AddOptions<TadssSettings>().Bind(configuration.GetSection(TadssSettings.SectionName));
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        return provider.GetRequiredService<IOptionsMonitor<TadssSettings>>();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + ReloadBudget;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }
        return condition();
    }

    [Fact]
    public void AddSmashCourtRuntimeOverrides_WhenDirectoryMissing_AddsNoSource()
    {
        var builder = new ConfigurationBuilder();

        builder.AddSmashCourtRuntimeOverrides(_contentRoot);

        Assert.Empty(builder.Sources);
    }

    [Fact]
    public void AddSmashCourtRuntimeOverrides_WhenDirectoryHasNoFile_KeepsEarlierValue()
    {
        Directory.CreateDirectory(FlagDirectory);

        var monitor = Monitor(earlier: true);

        Assert.True(monitor.CurrentValue.EnableTadssDecision);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AddSmashCourtRuntimeOverrides_WhenFileSetsFlag_OverridesEarlierSources(bool earlier, bool inFile)
    {
        Directory.CreateDirectory(FlagDirectory);
        File.WriteAllText(FlagFile, FlagJson(inFile));

        var monitor = Monitor(earlier);

        Assert.Equal(inFile, monitor.CurrentValue.EnableTadssDecision);
    }

    [Fact]
    public async Task AddSmashCourtRuntimeOverrides_WhenFileOverwrittenInPlace_ReloadsWithinTwoSeconds()
    {
        Directory.CreateDirectory(FlagDirectory);
        File.WriteAllText(FlagFile, FlagJson(false));
        var monitor = Monitor(earlier: false);

        File.WriteAllText(FlagFile, FlagJson(true));

        Assert.True(await WaitUntilAsync(() => monitor.CurrentValue.EnableTadssDecision));
    }

    // Cách deploy/tadss-flag.sh ghi: file tạm rồi mv đè lên file cờ, lần đầu file cờ chưa có
    [Fact]
    public async Task AddSmashCourtRuntimeOverrides_WhenFileCreatedOrReplacedByRename_ReloadsWithinTwoSeconds()
    {
        Directory.CreateDirectory(FlagDirectory);
        var monitor = Monitor(earlier: false);
        var temp = FlagFile + ".tmp";

        File.WriteAllText(temp, FlagJson(true));
        File.Move(temp, FlagFile, overwrite: true);
        Assert.True(await WaitUntilAsync(() => monitor.CurrentValue.EnableTadssDecision));

        File.WriteAllText(temp, FlagJson(false));
        File.Move(temp, FlagFile, overwrite: true);
        Assert.True(await WaitUntilAsync(() => !monitor.CurrentValue.EnableTadssDecision));
    }

    [Fact]
    public async Task AddSmashCourtRuntimeOverrides_WhenFileDeleted_FallsBackToEarlierValue()
    {
        Directory.CreateDirectory(FlagDirectory);
        File.WriteAllText(FlagFile, FlagJson(true));
        var monitor = Monitor(earlier: false);
        Assert.True(monitor.CurrentValue.EnableTadssDecision);

        File.Delete(FlagFile);

        Assert.True(await WaitUntilAsync(() => !monitor.CurrentValue.EnableTadssDecision));
    }

    [Fact]
    public void DeployCompose_MountsRuntimeConfigVolumeAndDoesNotPinFlagByEnvironment()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "deploy", "docker-compose.yml"));

        Assert.Contains("be-runtime-config:/app/runtime-config", compose);
        Assert.Contains("name: smashcourt-deploy-be-runtime-config", compose);
        // Biến môi trường thua file (file là nguồn cuối) nhưng sẽ quyết định khi file chưa có: không ghim cờ ở compose để tránh hiểu nhầm
        Assert.DoesNotMatch(new Regex(@"^\s*-?\s*TadssConfig__EnableTadssDecision\s*[:=]", RegexOptions.Multiline), compose);
    }

    [Fact]
    public void Dockerfile_CreatesRuntimeConfigDirectoryBeforeSwitchingToAppUser()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepoRoot(), "Dockerfile"));

        var mkdir = dockerfile.IndexOf("mkdir -p /app/runtime-config", StringComparison.Ordinal);
        var user = dockerfile.IndexOf("USER $APP_UID", StringComparison.Ordinal);
        Assert.True(mkdir >= 0, "Dockerfile phải tạo /app/runtime-config");
        Assert.True(mkdir < user, "Thư mục cờ phải tạo trước USER $APP_UID (cần quyền root để chown)");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SmashCourt-BE.csproj")))
                return dir.FullName;
        throw new InvalidOperationException("Không tìm thấy thư mục SmashCourt-BE (chứa SmashCourt-BE.csproj).");
    }
}
