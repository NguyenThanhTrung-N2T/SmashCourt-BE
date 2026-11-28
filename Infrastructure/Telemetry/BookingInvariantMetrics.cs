using System.Diagnostics.Metrics;
using SmashCourt_BE.Models.Invariants;

namespace SmashCourt_BE.Infrastructure.Telemetry;

/// <summary>
/// Metric của job kiểm tính đúng dữ liệu đặt sân (singleton). Gauge giữ kết quả của lượt kiểm gần nhất theo từng điều, kể cả điều có 0 vi phạm,
/// để luật cảnh báo luôn có chuỗi số liệu; chưa có lượt nào thì không xuất số.
/// </summary>
public sealed class BookingInvariantMetrics
{
    public const string MeterName = "SmashCourt.Booking";
    public const string ViolationsInstrument = "smashcourt.booking.invariant_violations";
    public const string CheckedBookingsInstrument = "smashcourt.booking.invariant_checked_bookings";
    public const string RunsInstrument = "smashcourt.booking.invariant_checks";

    public const string OutcomeClean = "clean";
    public const string OutcomeViolations = "violations";
    public const string OutcomeError = "error";

    private readonly Counter<long> _runs;
    private readonly object _gate = new();
    private Dictionary<string, long>? _lastViolations;
    private long? _lastChecked;
    private HashSet<(string Rule, Guid BookingId)> _lastKeys = [];

    public BookingInvariantMetrics(IMeterFactory meterFactory)
    {
        // Meter thuộc IMeterFactory (factory dispose khi host dừng), không dispose ở đây
        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge(ViolationsInstrument, ObserveViolations,
            description: "Số vi phạm tính đúng dữ liệu đặt sân ở lượt kiểm gần nhất, nhãn rule = mã điều, rule_name = tên dễ hiểu của điều.");
        meter.CreateObservableGauge(CheckedBookingsInstrument, ObserveChecked,
            description: "Số đơn trong phạm vi của lượt kiểm tính đúng gần nhất (đơn còn hiệu lực cộng đơn tạo hoặc sửa trong 24 giờ).");
        _runs = meter.CreateCounter<long>(RunsInstrument,
            description: "Số lượt kiểm tính đúng dữ liệu đặt sân, nhãn outcome = clean | violations | error.");
    }

    // Ghi kết quả một lượt kiểm thành công: mọi điều đều có số (0 khi sạch). Trả các vi phạm chưa có ở lượt thành công trước (theo điều và đơn)
    // để chỉ vi phạm mới được log Error; tiến trình khởi động lại thì mọi vi phạm đều tính là mới
    public IReadOnlyList<InvariantViolation> RecordRun(InvariantCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var counts = InvariantRules.DatabaseRules.ToDictionary(rule => rule, _ => 0L, StringComparer.Ordinal);
        foreach (var violation in result.Violations)
            counts[violation.Rule] = counts.GetValueOrDefault(violation.Rule) + 1;

        var keys = result.Violations.Select(v => (v.Rule, v.BookingId)).ToHashSet();
        IReadOnlyList<InvariantViolation> fresh;
        lock (_gate)
        {
            fresh = result.Violations.Where(v => !_lastKeys.Contains((v.Rule, v.BookingId))).ToList();
            _lastViolations = counts;
            _lastChecked = result.CheckedBookings;
            _lastKeys = keys;
        }
        _runs.Add(1, new KeyValuePair<string, object?>("outcome", result.Violations.Count == 0 ? OutcomeClean : OutcomeViolations));
        return fresh;
    }

    // Lượt kiểm lỗi (DB không đọc được…): giữ số của lượt trước, chỉ đếm lỗi
    public void RecordError() => _runs.Add(1, new KeyValuePair<string, object?>("outcome", OutcomeError));

    private IEnumerable<Measurement<long>> ObserveViolations()
    {
        lock (_gate)
        {
            if (_lastViolations == null)
                return [];
            // rule_name: tên dễ hiểu cho chú giải dashboard; rule giữ cho luật cảnh báo và truy vết
            return _lastViolations
                .Select(pair => new Measurement<long>(pair.Value,
                    new KeyValuePair<string, object?>("rule", pair.Key),
                    new KeyValuePair<string, object?>("rule_name", InvariantRules.Title(pair.Key))))
                .ToList();
        }
    }

    private IEnumerable<Measurement<long>> ObserveChecked()
    {
        lock (_gate)
            return _lastChecked is { } count ? [new Measurement<long>(count)] : [];
    }
}
