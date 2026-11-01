using System.Diagnostics;
using OpenTelemetry.Trace;
using SmashCourt_BE.Infrastructure.Telemetry;

namespace SmashCourt_BE.Tests.Unit.Infrastructure;

/// <summary>
/// OB-BE-2 (Epic 6C, gói 6C-3a): trace gốc chỉ lấy từ request vào BE. Span gốc không phải Server (Hangfire hỏi DB mỗi vài giây, job gọi HTTP)
/// bị bỏ để Grafana Cloud không ngập trace nền; span con theo quyết định của cha nên một lượt đặt sân vẫn đủ BE ⇒ DB ⇒ TADSS.
/// </summary>
[Trait("Catalog", "OB-BE-2")]
public class RequestRootSamplerTests
{
    private static SamplingParameters Root(ActivityKind kind) =>
        new(default, ActivityTraceId.CreateRandom(), "span", kind);

    [Fact]
    public void ShouldSample_WhenRootServerSpanAndRatioOne_RecordsAndSamples()
    {
        var result = new RequestRootSampler(1.0).ShouldSample(Root(ActivityKind.Server));

        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }

    [Theory]
    [InlineData(ActivityKind.Client)]
    [InlineData(ActivityKind.Internal)]
    [InlineData(ActivityKind.Producer)]
    [InlineData(ActivityKind.Consumer)]
    public void ShouldSample_WhenRootSpanIsNotServer_Drops(ActivityKind kind)
    {
        var result = new RequestRootSampler(1.0).ShouldSample(Root(kind));

        Assert.Equal(SamplingDecision.Drop, result.Decision);
    }

    [Fact]
    public void ShouldSample_WhenRootServerSpanAndRatioZero_Drops()
    {
        var result = new RequestRootSampler(0.0).ShouldSample(Root(ActivityKind.Server));

        Assert.Equal(SamplingDecision.Drop, result.Decision);
    }

    [Fact]
    public void ParentBased_WhenParentSampled_ChildClientSpanIsSampled()
    {
        var sampler = new ParentBasedSampler(new RequestRootSampler(1.0));
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);

        var result = sampler.ShouldSample(new SamplingParameters(parent, parent.TraceId, "SELECT", ActivityKind.Client));

        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
    }
}
