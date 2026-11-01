using System.Diagnostics;
using OpenTelemetry.Trace;

namespace SmashCourt_BE.Infrastructure.Telemetry;

/// <summary>
/// Sampler cho span gốc (dùng trong <see cref="ParentBasedSampler"/>): chỉ trace bắt đầu từ request vào BE (span Server) mới được lấy, theo tỷ lệ cấu hình.
/// Span gốc khác (Hangfire hỏi DB mỗi vài giây, job gọi HTTP) bị bỏ để trace nền không lấn trace của người dùng (Epic 6C, gói 6C-3a, OB-BE-2).
/// </summary>
public sealed class RequestRootSampler(double ratio) : Sampler
{
    private readonly TraceIdRatioBasedSampler _ratio = new(ratio);

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.Kind == ActivityKind.Server
            ? _ratio.ShouldSample(samplingParameters)
            : new SamplingResult(SamplingDecision.Drop);
}
