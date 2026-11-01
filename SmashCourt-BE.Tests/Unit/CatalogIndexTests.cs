using System.Text.Json;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Tests.Unit.Configurations;

namespace SmashCourt_BE.Tests.Unit;

/// <summary>
/// Chỉ mục test → mã danh mục cho dashboard "Kiểm thử" (Epic 6C, task 2.3): trx không giữ trait nên CI chạy test với
/// <c>SMASHCOURT_CATALOG_INDEX_DIR</c> để xuất <c>catalog-index.SmashCourt-BE.Tests.json</c>, rồi <c>TADSS-Runtime/perf/board.py</c> ghép với trx.
/// </summary>
[Trait("Catalog", "OB-BE-3")]
public class CatalogIndexTests
{
    private static readonly System.Reflection.Assembly TestAssembly = typeof(CatalogIndexTests).Assembly;

    [Fact]
    public void Build_TraitOnClass_AppliesToEveryTestMethodOfClass()
    {
        var index = CatalogIndex.Build(TestAssembly);

        var key = $"{typeof(TelemetrySettingsValidatorTests).FullName}.{nameof(TelemetrySettingsValidatorTests.Validate_WhenDefaults_IsDisabledAndValid)}";
        Assert.Equal(["OB-BE-1"], index[key]);
    }

    [Fact]
    public void Build_OnlyTestMethods_AreIndexed()
    {
        var index = CatalogIndex.Build(TestAssembly);

        Assert.Contains($"{typeof(CatalogIndexTests).FullName}.{nameof(Build_OnlyTestMethods_AreIndexed)}", index.Keys);
        Assert.DoesNotContain(index.Keys, key => key.EndsWith(".Equals", StringComparison.Ordinal) || key.EndsWith(".ToString", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_WhenDirectoryGiven_WritesEveryTestWithItsCodes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "smashcourt-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(CatalogIndex.Export(TestAssembly, null));

            var path = CatalogIndex.Export(TestAssembly, directory);

            Assert.Equal(Path.Combine(directory, "catalog-index.SmashCourt-BE.Tests.json"), path);
            var exported = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path!))!;
            Assert.Equal(CatalogIndex.Build(TestAssembly).Keys.Order(), exported.Keys.Order());
            Assert.Equal(["OB-BE-3"], exported[$"{typeof(CatalogIndexTests).FullName}.{nameof(Export_WhenDirectoryGiven_WritesEveryTestWithItsCodes)}"]);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>CI đặt biến môi trường ⇒ test này ghi chỉ mục của cả assembly; chạy trên máy không đặt thì không ghi gì.</summary>
    [Fact]
    public void ExportIfRequested_WritesIndexOnlyWhenVariableIsSet()
    {
        var directory = Environment.GetEnvironmentVariable(CatalogIndex.IndexDirectoryVariable);

        var path = CatalogIndex.ExportIfRequested(TestAssembly);

        if (string.IsNullOrWhiteSpace(directory))
            Assert.Null(path);
        else
            Assert.True(File.Exists(path));
    }
}
