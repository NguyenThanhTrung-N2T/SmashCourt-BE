using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using SmashCourt_BE.Data;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Chạy nguyên văn truy vấn export Phase 5 (<c>TADSS-Runtime/tools/evidence/sql/export_smashcourt_bookings.sql</c>) trong giao dịch của test
/// để đối chiếu dữ liệu BE dựng với bản gốc. Chỉ có khi thư mục TADSS-Runtime nằm cạnh repo BE (máy dev).
/// </summary>
internal static class Phase5ExportSql
{
    public static string? FindPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "TADSS-Runtime", "tools", "evidence", "sql", "export_smashcourt_bookings.sql");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Trả các dòng của booking trong <paramref name="bookingIds"/>, khóa theo tên cột (NULL của SQL thành null).</summary>
    public static async Task<Dictionary<Guid, Dictionary<string, object?>>> RunAsync(
        SmashCourtContext db, string sqlPath, ISet<Guid> bookingIds)
    {
        // Bỏ BEGIN/COMMIT vì test đã nằm trong một giao dịch
        var lines = (await File.ReadAllLinesAsync(sqlPath))
            .Where(line => !line.StartsWith("BEGIN TRANSACTION", StringComparison.Ordinal) && line.Trim() != "COMMIT;");

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(string.Join(Environment.NewLine, lines), connection)
        {
            Transaction = (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction()
        };
        await using var reader = await command.ExecuteReaderAsync();

        var result = new Dictionary<Guid, Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(reader.GetOrdinal("booking_id"));
            if (!bookingIds.Contains(id))
                continue;
            result[id] = Enumerable.Range(0, reader.FieldCount)
                .ToDictionary(i => reader.GetName(i), i => reader.IsDBNull(i) ? null : reader.GetValue(i));
        }

        return result;
    }
}
