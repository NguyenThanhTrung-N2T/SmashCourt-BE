namespace SmashCourt_BE.Configurations;

/// <summary>
/// OpenTelemetry của BE (Epic 6C, gói 6C-3a, OB-BE-1) — section "Telemetry". Mặc định <b>tắt</b>: dev và host test không gửi gì.
/// Bản deploy bật bằng biến môi trường và gửi OTLP tới Alloy (<c>deploy/docker-compose.yml</c>), Alloy đẩy lên Grafana Cloud.
/// </summary>
public class TelemetrySettings
{
    public const string SectionName = "Telemetry";

    public bool Enabled { get; set; }
    /// <summary>Địa chỉ OTLP gRPC của collector.</summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
    /// <summary>Tên dịch vụ (nhãn <c>job</c> trên Grafana).</summary>
    public string ServiceName { get; set; } = "smashcourt-be";
    /// <summary>Chu kỳ gửi metric (ms). Grafana Cloud miễn phí tính theo số điểm mỗi phút nên bản deploy đặt 60000.</summary>
    public int MetricExportIntervalMs { get; set; } = 15000;
    /// <summary>Tỷ lệ lấy mẫu trace gốc (0…1); span con theo quyết định của span cha.</summary>
    public double TraceSampleRatio { get; set; } = 1.0;
    /// <summary>Gửi log tới collector (ngoài console).</summary>
    public bool ExportLogs { get; set; } = true;
}
