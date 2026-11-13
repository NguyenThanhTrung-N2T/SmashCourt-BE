using OpenTelemetry;
using OpenTelemetry.Metrics;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>Chạy riêng, không song song: provider OpenTelemetry nghe meter theo tên nên sẽ bắt cả mạch của test khác đang chạy cùng lúc.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TadssCircuitMetricsCollection
{
    public const string Name = "TadssCircuitMetrics";
}

/// <summary>BE-UT-14 (P6-57): metric của mạch được xuất qua đúng đường <see cref="TelemetryExtensions.AddSmashCourtMeters"/> (meter đã đăng ký).</summary>
[Collection(TadssCircuitMetricsCollection.Name)]
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-14")]
public sealed class TadssCircuitMetricsTests
{
    [Fact]
    public void Metrics_AreExportedThroughTheSmashCourtMeterPipeline()
    {
        var exported = new List<Metric>();
        using (var provider = Sdk.CreateMeterProviderBuilder().AddSmashCourtMeters().AddInMemoryExporter(exported).Build())
        {
            using var circuit = TadssCircuitFactory.Create(new TestOptionsMonitor<TadssSettings>(TadssCircuitFactory.EnabledSettings()), TimeProvider.System);
            circuit.RecordAttempt(TadssCircuitFactory.Acquire(circuit), TadssAttemptOutcome.Failure);
            circuit.RecordProbe(healthy: false);
            provider.ForceFlush();
        }

        // ForceFlush và lúc dispose provider đều xuất một lần nên mỗi metric có thể xuất hiện hai lần trong danh sách
        Assert.Contains(exported, m => m.Name == TadssCircuit.StateInstrument);
        var points = new List<(string? To, long Value)>();
        foreach (var transitions in exported.Where(m => m.Name == TadssCircuit.TransitionsInstrument))
        {
            foreach (ref readonly var point in transitions.GetMetricPoints())
            {
                string? to = null;
                foreach (var tag in point.Tags)
                    if (tag.Key == "to")
                        to = tag.Value as string;
                points.Add((to, point.GetSumLong()));
            }
        }
        Assert.Contains(("open", 1L), points);
    }
}
