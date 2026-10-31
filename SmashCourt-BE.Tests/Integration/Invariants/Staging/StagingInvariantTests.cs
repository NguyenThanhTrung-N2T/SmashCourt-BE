using Xunit.Abstractions;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>
/// BE-IT-20 trên staging (plan 6A-6 Task 11, chạy ở Phase 7): <c>dotnet test SmashCourt-BE.Tests --filter "Category=Staging"</c> kèm
/// <c>SC6_CONNECTION_STRING</c>, <c>SC6_FROM_UTC</c>, <c>SC6_TO_UTC</c>, <c>TADSS_BASE_URL</c>, <c>TADSS_API_KEY</c>, <c>SC6_OUTPUT</c>.
/// Chỉ đạt khi báo cáo <c>PASS</c> (0 vi phạm và 0 mục không kết luận được).
/// </summary>
[Trait("Category", "Staging")]
[Trait("Catalog", "BE-IT-20")]
public sealed class StagingInvariantTests(ITestOutputHelper output)
{
    [StagingFact]
    public async Task Sc6_OnStaging_Passes()
    {
        var report = await StagingInvariantRunner.RunAsync(StagingRunOptions.FromEnvironment());

        output.WriteLine(report.ToJson());
        Assert.True(report.Status == StagingInvariantReport.Pass,
            $"SC6 {report.Status}: {report.Violations.Count} vi phạm, {report.InconclusiveChecks.Count} mục không kết luận được" +
            (report.AbortReason == null ? "" : $", dừng vì: {report.AbortReason}"));
    }
}
