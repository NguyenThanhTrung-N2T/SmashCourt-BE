using System.Text.Json;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>Báo cáo lượt kiểm staging (plan 6A-6 Task 11, R28): luật trạng thái, đếm theo điều, ghi file một lần.</summary>
[Trait("Catalog", "BE-IT-20")]
public sealed class StagingInvariantReportTests : IDisposable
{
    private static readonly ReportWindow Window = new(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc));
    private static readonly DateTime Started = new(2026, 12, 3, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sc6-report-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static StagingInvariantReport Report(
        IReadOnlyList<InvariantViolation>? violations = null, IReadOnlyList<InconclusiveCheck>? inconclusive = null,
        IReadOnlyList<OrphanDecision>? orphans = null, string? orphanError = null) =>
        new(Window, Started, Started.AddSeconds(3), 10, 4, violations ?? [], inconclusive ?? [], orphans ?? [], orphanError);

    private static InvariantViolation V(string rule) => new(rule, Guid.NewGuid(), "x");

    [Fact]
    public void Status_PassOnlyWithoutViolationsAndInconclusiveChecks()
    {
        Assert.Equal(StagingInvariantReport.Pass, Report().Status);
        Assert.Equal(StagingInvariantReport.Fail, Report([V("SC6-1")]).Status);
        Assert.Equal(StagingInvariantReport.Fail,
            Report([V("SC6-5")], [new InconclusiveCheck("SC6-5", Guid.NewGuid(), Guid.NewGuid(), "HTTP 503")]).Status);
        Assert.Equal(StagingInvariantReport.Inconclusive,
            Report(inconclusive: [new InconclusiveCheck("SC6-5", Guid.NewGuid(), Guid.NewGuid(), "HTTP 503")]).Status);
    }

    [Fact]
    public void OrphanDecisions_AreDiagnosticsOnly()
    {
        var report = Report(orphans: [new OrphanDecision(Guid.NewGuid(), Guid.NewGuid(), "đơn BE không lưu tadss_decision_id")],
            orphanError: "Không đọc được tadss.decision_executions");

        Assert.Equal(StagingInvariantReport.Pass, report.Status);
    }

    [Fact]
    public void Aborted_IsNeverPass()
    {
        var report = StagingInvariantReport.Aborted(Window, Started, Started.AddSeconds(1), "NpgsqlException: mất kết nối");

        Assert.Equal(StagingInvariantReport.Inconclusive, report.Status);
        Assert.Equal("NpgsqlException: mất kết nối", report.AbortReason);
    }

    [Fact]
    public void ViolationsByRule_CountsEachRule()
    {
        var report = Report([V("SC6-3"), V("SC6-1"), V("SC6-3")]);

        Assert.Equal(new Dictionary<string, int> { ["SC6-1"] = 1, ["SC6-3"] = 2 }, report.ViolationsByRule);
    }

    [Fact]
    public void Json_HasEveryReportField()
    {
        using var json = JsonDocument.Parse(Report([V("SC6-6")]).ToJson());
        var root = json.RootElement;

        foreach (var field in new[] { "window", "startedAt", "finishedAt", "checkedBookings", "checkedDecisions", "status", "violationsByRule",
                     "violations", "inconclusiveChecks", "orphanDecisions", "orphanCheckError", "abortReason" })
            Assert.True(root.TryGetProperty(field, out _), $"thiếu trường {field}");
        Assert.Equal("FAIL", root.GetProperty("status").GetString());
        Assert.Equal("SC6-6", root.GetProperty("violations")[0].GetProperty("rule").GetString());
        Assert.Equal(10, root.GetProperty("checkedBookings").GetInt32());
    }

    [Fact]
    public async Task WriteAtomically_ReplacesTheFileAndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(_dir, "nested", "sc6.json");
        await Report([V("SC6-1")]).WriteAtomicallyAsync(path);
        await Report().WriteAtomicallyAsync(path);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal("PASS", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(["sc6.json"], Directory.GetFiles(Path.Combine(_dir, "nested")).Select(Path.GetFileName));
    }
}
