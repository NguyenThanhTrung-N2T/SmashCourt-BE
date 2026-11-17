using OpenTelemetry;
using OpenTelemetry.Metrics;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Infrastructure.Telemetry;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Infrastructure;

/// <summary>Chạy riêng, không song song: provider OpenTelemetry nghe meter theo tên nên sẽ bắt cả instance của test khác đang chạy cùng lúc.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BookingInvariantMetricsCollection
{
    public const string Name = "BookingInvariantMetrics";
}

/// <summary>Metric kiểm tính đúng được xuất qua đúng đường <see cref="TelemetryExtensions.AddSmashCourtMeters"/> (meter đã đăng ký), với đủ nhãn theo điều.</summary>
[Collection(BookingInvariantMetricsCollection.Name)]
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "OB-BE-5")]
public sealed class BookingInvariantMetricsTests
{
    [Fact]
    public void Metrics_AreExportedThroughTheSmashCourtMeterPipeline()
    {
        var exported = new List<Metric>();
        using (var provider = Sdk.CreateMeterProviderBuilder().AddSmashCourtMeters().AddInMemoryExporter(exported).Build())
        {
            var metrics = new BookingInvariantMetrics(MeterProbe.NewFactory());
            metrics.RecordRun(new InvariantCheckResult(4, [new InvariantViolation(InvariantRules.NoDoubleBooking, Guid.NewGuid(), "trùng giờ")]));
            provider.ForceFlush();
        }

        // ForceFlush và lúc dispose provider đều xuất một lần nên mỗi metric có thể xuất hiện hai lần trong danh sách
        var rules = new Dictionary<string, long>();
        foreach (var gauge in exported.Where(m => m.Name == BookingInvariantMetrics.ViolationsInstrument))
        {
            foreach (ref readonly var point in gauge.GetMetricPoints())
            {
                foreach (var tag in point.Tags)
                    if (tag.Key == "rule")
                        rules[(string)tag.Value!] = point.GetGaugeLastValueLong();
            }
        }

        Assert.Equal(InvariantRules.DatabaseRules.Count, rules.Count);
        Assert.Equal(1, rules[InvariantRules.NoDoubleBooking]);
        Assert.Contains(exported, m => m.Name == BookingInvariantMetrics.CheckedBookingsInstrument);
        Assert.Contains(exported, m => m.Name == BookingInvariantMetrics.RunsInstrument);
    }
}
