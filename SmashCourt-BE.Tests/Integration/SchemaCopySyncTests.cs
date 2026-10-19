namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Bản sao schema trong TestData/Database phải khớp bản chuẩn ở TADSS-Runtime/docs/database (dump từ Neon).
/// Chỉ chạy được khi có thư mục TADSS-Runtime cạnh repo BE (máy dev); trên CI chỉ checkout BE nên bỏ qua.
/// </summary>
public sealed class SchemaCopySyncTests
{
    [Fact]
    public void SchemaCopy_WhenTadssRepoIsPresent_MatchesCanonicalDump()
    {
        var canonical = FindCanonicalSchema();
        if (canonical == null)
            return; // Không có TADSS-Runtime cạnh repo BE (CI)

        var copy = Normalize(File.ReadAllText(PostgreSqlIntegrationFixture.SchemaFilePath));
        var source = Normalize(File.ReadAllText(canonical));

        Assert.True(source == copy,
            "Bản sao schema lệch bản chuẩn. Cập nhật: chép TADSS-Runtime/docs/database/schema_smashcourt.sql " +
            "đè lên SmashCourt-BE.Tests/TestData/Database/schema_smashcourt.sql.");
    }

    // Bỏ BOM, CRLF và dòng restrict / unrestrict của pg_dump (khóa ngẫu nhiên đổi mỗi lần dump, bắt đầu bằng dấu gạch chéo ngược)
    private static string Normalize(string text)
    {
        const char backslash = (char)92;
        var lines = text.TrimStart((char)0xFEFF).Replace(((char)13).ToString(), string.Empty).Split((char)10)
            .Where(l => !l.StartsWith(backslash + "restrict", StringComparison.Ordinal)
                        && !l.StartsWith(backslash + "unrestrict", StringComparison.Ordinal))
            .Select(l => l.TrimEnd());
        return string.Join((char)10, lines).Trim();
    }

    private static string? FindCanonicalSchema()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TADSS-Runtime", "docs", "database", "schema_smashcourt.sql");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
