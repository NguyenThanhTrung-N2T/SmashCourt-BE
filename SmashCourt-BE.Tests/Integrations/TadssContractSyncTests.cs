using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integrations;

/// <summary>
/// Bản sao JSON schema v2 trong TestData/Tadss/v2/schemas phải khớp bản chuẩn ở TADSS-Runtime/docs/contracts/schemas/v2.
/// Chỉ chạy được khi có thư mục TADSS-Runtime cạnh repo BE (máy dev); trên CI chỉ checkout BE nên bỏ qua.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "CT-04")]
public class TadssContractSyncTests
{
    [Fact]
    public void SchemaCopies_WhenTadssRepoIsPresent_MatchCanonicalSchemas()
    {
        var canonicalDir = FindCanonicalSchemaDirectory();
        if (canonicalDir == null)
            return; // Không có TADSS-Runtime cạnh repo BE (CI)

        var copyDir = Path.Combine(TadssContractFiles.Root, "schemas");
        var canonicalNames = Directory.GetFiles(canonicalDir, "*.json").Select(Path.GetFileName).Order().ToList();
        var copyNames = Directory.GetFiles(copyDir, "*.json").Select(Path.GetFileName).Order().ToList();

        Assert.Equal(canonicalNames, copyNames);
        foreach (var name in canonicalNames)
        {
            Assert.True(
                Normalize(File.ReadAllText(Path.Combine(canonicalDir, name!))) == Normalize(File.ReadAllText(Path.Combine(copyDir, name!))),
                $"Bản sao schema {name} lệch bản chuẩn. Cập nhật: chép TADSS-Runtime/docs/contracts/schemas/v2/{name} " +
                "đè lên SmashCourt-BE.Tests/TestData/Tadss/v2/schemas/.");
        }
    }

    // Bỏ BOM và khác biệt CRLF/LF, khoảng trắng cuối dòng
    private static string Normalize(string text)
    {
        var lines = text.TrimStart((char)0xFEFF).Replace(((char)13).ToString(), string.Empty).Split((char)10)
            .Select(line => line.TrimEnd());
        return string.Join((char)10, lines).Trim();
    }

    private static string? FindCanonicalSchemaDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TADSS-Runtime", "docs", "contracts", "schemas", "v2");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
