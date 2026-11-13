using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Mạch ngắt của lệnh đánh giá cọc (P6-57, plan 6C). Khi TADSS treo, mỗi đơn online từng phải chờ 800 ms × 2 rồi mới cọc 100 %
/// (SP-09). Mạch mở khi một lần gọi lỗi và lần kiểm <c>/health/ready</c> xác nhận cũng lỗi, hoặc khi đủ <c>CircuitFailureThreshold</c> lần
/// gọi lỗi liên tiếp; lúc mở mọi đơn cọc 100 % ngay. Mạch chỉ đóng khi một <b>đơn thật đi thử</b> thành công. Đơn thử được phép khi lần kiểm
/// nền chuyển từ lỗi sang tốt, hoặc khi tới mốc lịch (60 → 120 → 240 → 300 s theo cấu hình); kiểm sức khỏe chỉ để mở nhanh và cho thử sớm.
/// Mạch tắt (<c>CircuitEnabled</c> hoặc cờ TADSS tắt) thì hành vi đúng như trước; cấu hình đổi thì mạch về đóng.
/// Đếm theo lần gọi: chỉ timeout, lỗi mạng, 5xx (<see cref="Classify{T}"/>). Trạng thái theo từng tiến trình BE.
/// </summary>
public sealed class TadssCircuit : ITadssCircuit, IDisposable
{
    public const string MeterName = "SmashCourt.Tadss";
    public const string StateInstrument = "smashcourt.tadss.circuit.state";
    public const string TransitionsInstrument = "smashcourt.tadss.circuit.transitions";

    private readonly IOptionsMonitor<TadssSettings> _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<TadssCircuit> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _transitions;
    private readonly IDisposable? _subscription;
    private readonly SemaphoreSlim _probeRequested = new(0, 1);
    private readonly object _gate = new();

    private bool _enabled;
    private TadssCircuitState _state = TadssCircuitState.Closed;
    private int _consecutiveFailures;
    private bool _confirmationPending;
    private bool? _lastProbeHealthy;
    private bool _trialInFlight;
    private int _breakLevel;
    private DateTimeOffset _trialDueAt;

    public TadssCircuit(
        IOptionsMonitor<TadssSettings> settings, TimeProvider time, ILogger<TadssCircuit> logger, IMeterFactory meterFactory)
    {
        _settings = settings;
        _time = time;
        _logger = logger;
        _enabled = IsEnabled(settings.CurrentValue);
        _meter = meterFactory.Create(MeterName);
        _meter.CreateObservableGauge(StateInstrument, () => (int)State,
            description: "Trạng thái mạch ngắt lệnh đánh giá cọc của BE: 0 đóng, 1 mở, 2 nửa mở (P6-57).");
        _transitions = _meter.CreateCounter<long>(TransitionsInstrument,
            description: "Số lần mạch ngắt lệnh đánh giá cọc chuyển trạng thái, nhãn to = trạng thái đích.");
        _subscription = settings.OnChange(OnSettingsChanged);
    }

    public TadssCircuitState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>Tiến trình kiểm nền có việc: đang chờ kiểm xác nhận, hoặc mạch không đóng.</summary>
    public bool ShouldProbe
    {
        get { lock (_gate) return _enabled && (_confirmationPending || _state != TadssCircuitState.Closed); }
    }

    public TimeSpan ProbeInterval => TimeSpan.FromSeconds(_settings.CurrentValue.CircuitProbeIntervalSeconds);

    /// <summary>Chờ tới khi có yêu cầu kiểm xác nhận (sau lần gọi lỗi đầu tiên của một chuỗi).</summary>
    public Task WaitForProbeRequestAsync(CancellationToken cancellationToken) => _probeRequested.WaitAsync(cancellationToken);

    /// <summary>Phân loại kết quả một lần gọi theo cách mạch đếm.</summary>
    public static TadssAttemptOutcome Classify<T>(TadssCallResult<T> result) => result.IsSuccess
        ? TadssAttemptOutcome.Success
        : result.FailureKind switch
        {
            TadssFailureKind.Timeout or TadssFailureKind.Network => TadssAttemptOutcome.Failure,
            TadssFailureKind.Http when result.HttpStatus >= 500 => TadssAttemptOutcome.Failure,
            _ => TadssAttemptOutcome.Neutral
        };

    public TadssCircuitPermit? TryAcquire()
    {
        lock (_gate)
        {
            if (!_enabled)
                return TadssCircuitPermit.Untracked;
            AdvanceIfDue();
            if (_state == TadssCircuitState.Closed)
                return new TadssCircuitPermit(isTrial: false, tracked: true);
            if (_state == TadssCircuitState.HalfOpen && !_trialInFlight)
            {
                _trialInFlight = true;
                return new TadssCircuitPermit(isTrial: true, tracked: true);
            }
            return null;
        }
    }

    public bool CanRetry(TadssCircuitPermit permit)
    {
        if (!permit.Tracked)
            return true;
        if (permit.IsTrial)
            return false;
        lock (_gate)
            return !_enabled || _state == TadssCircuitState.Closed;
    }

    public void RecordAttempt(TadssCircuitPermit permit, TadssAttemptOutcome outcome)
    {
        if (!permit.Tracked)
            return;
        lock (_gate)
        {
            if (!_enabled)
                return;
            if (permit.IsTrial)
            {
                RecordTrial(permit, outcome);
                return;
            }
            if (outcome == TadssAttemptOutcome.Success)
                _consecutiveFailures = 0;
            else if (outcome == TadssAttemptOutcome.Failure)
                RecordFailure();
        }
    }

    public void Release(TadssCircuitPermit permit)
    {
        if (!permit.Tracked || !permit.IsTrial)
            return;
        lock (_gate)
        {
            if (permit.Resolved)
                return;
            permit.Resolved = true;
            _trialInFlight = false;
        }
    }

    /// <summary>Kết quả một lần kiểm <c>/health/ready</c> (xác nhận hoặc kiểm nền).</summary>
    public void RecordProbe(bool healthy)
    {
        lock (_gate)
        {
            if (!_enabled)
                return;
            var previous = _lastProbeHealthy;
            _lastProbeHealthy = healthy;
            if (_state == TadssCircuitState.Closed)
            {
                if (_confirmationPending)
                {
                    _confirmationPending = false;
                    if (!healthy && _consecutiveFailures > 0)
                        Open(0, "một lần gọi lỗi và kiểm /health/ready cũng lỗi");
                }
                return;
            }
            if (_state == TadssCircuitState.Open && healthy && previous == false)
                Move(TadssCircuitState.HalfOpen, "kiểm /health/ready chuyển từ lỗi sang tốt");
        }
    }

    /// <summary>Chuyển sang nửa mở khi tới mốc lịch (tiến trình kiểm nền gọi mỗi chu kỳ để gauge đúng cả lúc vắng đơn).</summary>
    public void Tick()
    {
        lock (_gate)
            AdvanceIfDue();
    }

    // Meter thuộc IMeterFactory (factory dispose khi host dừng), không dispose ở đây
    public void Dispose()
    {
        _subscription?.Dispose();
        _probeRequested.Dispose();
    }

    private void RecordTrial(TadssCircuitPermit permit, TadssAttemptOutcome outcome)
    {
        if (permit.Resolved)
            return;
        permit.Resolved = true;
        _trialInFlight = false;
        if (_state != TadssCircuitState.HalfOpen)
            return;
        if (outcome == TadssAttemptOutcome.Success)
            Close("đơn thử thành công");
        else if (outcome == TadssAttemptOutcome.Failure)
            Open(_breakLevel + 1, "đơn thử lỗi");
    }

    private void RecordFailure()
    {
        _consecutiveFailures++;
        if (_state != TadssCircuitState.Closed)
            return;
        var threshold = _settings.CurrentValue.CircuitFailureThreshold;
        if (_consecutiveFailures >= threshold)
        {
            Open(0, $"{_consecutiveFailures} lần gọi lỗi liên tiếp");
            return;
        }
        if (_consecutiveFailures == 1)
        {
            // Kiểm xác nhận chạy song song với lần thử lại của đơn đang lỗi; tiến trình nền chỉ chạy một lần kiểm tại một thời điểm
            _confirmationPending = true;
            if (_probeRequested.CurrentCount == 0)
                _probeRequested.Release();
        }
    }

    private void AdvanceIfDue()
    {
        if (_state == TadssCircuitState.Open && _time.GetUtcNow() >= _trialDueAt)
            Move(TadssCircuitState.HalfOpen, "tới mốc cho đơn đi thử");
    }

    private void Open(int breakLevel, string reason)
    {
        var settings = _settings.CurrentValue;
        _breakLevel = breakLevel;
        var seconds = Math.Min(settings.CircuitBreakSeconds * Math.Pow(2, breakLevel), settings.CircuitMaxBreakSeconds);
        _trialDueAt = _time.GetUtcNow().AddSeconds(seconds);
        _confirmationPending = false;
        Move(TadssCircuitState.Open, $"{reason}; đơn thử muộn nhất sau {seconds:0} s");
    }

    private void Close(string reason)
    {
        _consecutiveFailures = 0;
        _breakLevel = 0;
        _trialInFlight = false;
        _confirmationPending = false;
        _lastProbeHealthy = null;
        Move(TadssCircuitState.Closed, reason);
    }

    private void Move(TadssCircuitState to, string reason)
    {
        var from = _state;
        _state = to;
        _transitions.Add(1, new KeyValuePair<string, object?>("to", Label(to)));
        _logger.LogWarning("Mạch gọi TADSS: {From} ⇒ {To} ({Reason})", Describe(from), Describe(to), reason);
    }

    private void OnSettingsChanged(TadssSettings settings)
    {
        lock (_gate)
        {
            var enabled = IsEnabled(settings);
            if (enabled == _enabled)
                return;
            _enabled = enabled;
            if (_state != TadssCircuitState.Closed)
                Close(enabled ? "bật lại mạch ngắt" : "tắt mạch ngắt hoặc tắt cờ TADSS");
            else
            {
                _consecutiveFailures = 0;
                _confirmationPending = false;
                _lastProbeHealthy = null;
                _breakLevel = 0;
            }
        }
    }

    private static bool IsEnabled(TadssSettings settings) => settings.CircuitEnabled && settings.EnableTadssDecision;

    private static string Label(TadssCircuitState state) => state switch
    {
        TadssCircuitState.Open => "open",
        TadssCircuitState.HalfOpen => "half_open",
        _ => "closed"
    };

    private static string Describe(TadssCircuitState state) => state switch
    {
        TadssCircuitState.Open => "MỞ (đơn online cọc 100 %, không gọi TADSS)",
        TadssCircuitState.HalfOpen => "NỬA MỞ (một đơn đi thử)",
        _ => "ĐÓNG"
    };
}
