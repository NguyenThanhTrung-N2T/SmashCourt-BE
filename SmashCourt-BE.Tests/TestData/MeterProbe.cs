using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>
/// Nghe đúng một instance <see cref="Meter"/> (của <see cref="IMeterFactory"/> riêng của test) nên test chạy song song không bắt nhầm số của nhau.
/// Counter ghi ngay khi Add; gauge chỉ có số sau <see cref="ObserveGauges"/>.
/// </summary>
internal sealed class MeterProbe : IDisposable
{
    public sealed record Point(string Instrument, long Value, IReadOnlyDictionary<string, object?> Tags);

    private readonly MeterListener _listener = new();
    private readonly List<Point> _points = [];
    private readonly object _gate = new();

    public MeterProbe(IMeterFactory factory, string meterName)
    {
        // DefaultMeterFactory trả lại cùng instance cho cùng tên: chính meter mà lớp được kiểm đã tạo
        var meter = factory.Create(meterName);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags)
                tagMap[tag.Key] = tag.Value;
            lock (_gate)
                _points.Add(new Point(instrument.Name, value, tagMap));
        });
        _listener.Start();
    }

    public static IMeterFactory NewFactory() =>
        new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();

    // Đọc gauge một lần, trả về các điểm gauge của lần đọc đó
    public IReadOnlyList<Point> ObserveGauges(string instrument)
    {
        int before;
        lock (_gate)
            before = _points.Count;
        _listener.RecordObservableInstruments();
        lock (_gate)
            return _points.Skip(before).Where(p => p.Instrument == instrument).ToList();
    }

    public IReadOnlyList<Point> Recorded(string instrument)
    {
        lock (_gate)
            return _points.Where(p => p.Instrument == instrument).ToList();
    }

    public void Dispose() => _listener.Dispose();
}
