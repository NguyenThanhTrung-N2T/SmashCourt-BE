using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// BE-UT-14 (P6-57): chuyển trạng thái của mạch ngắt lệnh đánh giá cọc. Mở khi một lần gọi lỗi và kiểm sức khỏe cũng lỗi, hoặc 3 lần gọi lỗi
/// liên tiếp; nửa mở khi kiểm chuyển từ lỗi sang tốt hoặc tới mốc lịch 60 → 120 → 240 → 300 s; chỉ đóng khi đơn thử thành công.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-14")]
public sealed class TadssCircuitTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly TestOptionsMonitor<TadssSettings> _settings = new(TadssCircuitFactory.EnabledSettings());
    private readonly ListLogger<TadssCircuit> _logger = new();
    private readonly TadssCircuit _circuit;

    public TadssCircuitTests() => _circuit = TadssCircuitFactory.Create(_settings, _time, _logger);

    public void Dispose() => _circuit.Dispose();

    private void Fail() => _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Failure);

    private TadssCircuitPermit Acquire() =>
        _circuit.TryAcquire() ?? throw new InvalidOperationException("Mạch không cấp quyền gọi");

    // Mở bằng đường xác nhận: một lần gọi lỗi, kiểm sức khỏe cũng lỗi
    private void OpenByConfirmation()
    {
        Fail();
        _circuit.RecordProbe(healthy: false);
        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    private void Reconfigure(Action<TadssSettings> change)
    {
        var settings = TadssCircuitFactory.EnabledSettings();
        change(settings);
        _settings.Set(settings);
    }

    // ── Mạch tắt ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TryAcquire_WhenCircuitOrFlagDisabled_ReturnsUntrackedAndNeverOpens(bool circuitEnabled, bool flagEnabled)
    {
        Reconfigure(s => { s.CircuitEnabled = circuitEnabled; s.EnableTadssDecision = flagEnabled; });

        for (var i = 0; i < 10; i++)
        {
            var permit = _circuit.TryAcquire();
            Assert.Same(TadssCircuitPermit.Untracked, permit);
            _circuit.RecordAttempt(permit!, TadssAttemptOutcome.Failure);
        }

        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
        Assert.False(_circuit.ShouldProbe);
    }

    // ── Mở ───────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordAttempt_WhenFirstFailure_RequestsConfirmationProbeButStaysClosed()
    {
        Fail();

        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
        Assert.True(_circuit.ShouldProbe);
        Assert.True(_circuit.WaitForProbeRequestAsync(CancellationToken.None).IsCompletedSuccessfully);
    }

    [Fact]
    public void RecordProbe_WhenConfirmationFails_OpensAndShortCircuitsEveryBooking()
    {
        OpenByConfirmation();

        Assert.Null(_circuit.TryAcquire());
        Assert.Null(_circuit.TryAcquire());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("⇒ MỞ"));
    }

    [Fact]
    public void RecordProbe_WhenConfirmationHealthy_StaysClosedUntilThresholdOfConsecutiveFailures()
    {
        Fail();
        _circuit.RecordProbe(healthy: true);
        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
        Assert.False(_circuit.ShouldProbe);

        Fail();
        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
        Fail();

        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    [Fact]
    public void RecordAttempt_WhenSuccessBetweenFailures_ResetsTheConsecutiveCount()
    {
        Fail();
        _circuit.RecordProbe(healthy: true);
        Fail();
        _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Success);
        Fail();
        Fail();

        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
    }

    [Fact]
    public void RecordAttempt_WhenNeutralOutcomes_NeverCounts()
    {
        for (var i = 0; i < 10; i++)
            _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Neutral);

        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
        Assert.False(_circuit.ShouldProbe);
    }

    // ── Nửa mở và đóng ───────────────────────────────────────────────────────

    [Fact]
    public void RecordProbe_WhenProbeTurnsFromBadToGood_AllowsExactlyOneTrialEarly()
    {
        OpenByConfirmation();

        _circuit.RecordProbe(healthy: true);

        Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
        var trial = _circuit.TryAcquire();
        Assert.NotNull(trial);
        Assert.True(trial.IsTrial);
        Assert.Null(_circuit.TryAcquire());
    }

    [Fact]
    public void RecordProbe_WhenOpenedByConsecutiveFailuresWhileProbeHealthy_DoesNotAllowEarlyTrial()
    {
        Fail();
        _circuit.RecordProbe(healthy: true);
        Fail();
        Fail();
        Assert.Equal(TadssCircuitState.Open, _circuit.State);

        _circuit.RecordProbe(healthy: true);
        _time.Advance(TimeSpan.FromSeconds(59));
        _circuit.Tick();
        Assert.Equal(TadssCircuitState.Open, _circuit.State);

        _time.Advance(TimeSpan.FromSeconds(1));
        _circuit.Tick();
        Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
    }

    [Fact]
    public void RecordAttempt_WhenTrialSucceeds_Closes()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);

        _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Success);

        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
        Assert.False(Acquire().IsTrial);
    }

    [Fact]
    public void RecordAttempt_WhenTrialFailsWhileProbeHealthy_WaitsForScheduleUntilProbeGoesBadThenGood()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);
        _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Failure);
        Assert.Equal(TadssCircuitState.Open, _circuit.State);

        _circuit.RecordProbe(healthy: true);
        Assert.Equal(TadssCircuitState.Open, _circuit.State);

        _circuit.RecordProbe(healthy: false);
        _circuit.RecordProbe(healthy: true);
        Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
    }

    [Fact]
    public void Schedule_WhenTrialsKeepFailing_DoublesUpToTheCapAndResetsAfterClosing()
    {
        OpenByConfirmation();
        foreach (var expected in new[] { 60, 120, 240, 300, 300 })
        {
            _time.Advance(TimeSpan.FromSeconds(expected - 1));
            _circuit.Tick();
            Assert.Equal(TadssCircuitState.Open, _circuit.State);
            _time.Advance(TimeSpan.FromSeconds(1));
            _circuit.Tick();
            Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
            _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Failure);
        }

        _time.Advance(TimeSpan.FromSeconds(300));
        _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Success);
        Assert.Equal(TadssCircuitState.Closed, _circuit.State);

        OpenByConfirmation();
        _time.Advance(TimeSpan.FromSeconds(60));
        _circuit.Tick();
        Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
    }

    [Fact]
    public void Release_WhenTrialEndsWithoutResult_KeepsHalfOpenAndLetsTheNextBookingTry()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);
        var trial = Acquire();

        _circuit.Release(trial);

        Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
        Assert.True(Acquire().IsTrial);
    }

    [Fact]
    public void RecordAttempt_WhenTrialNeutral_ReleasesTheTrialWithoutClosing()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);

        _circuit.RecordAttempt(Acquire(), TadssAttemptOutcome.Neutral);

        Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
        Assert.True(Acquire().IsTrial);
    }

    [Fact]
    public void Release_AfterTrialRecorded_DoesNothing()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);
        var trial = Acquire();
        _circuit.RecordAttempt(trial, TadssAttemptOutcome.Failure);

        _circuit.Release(trial);

        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    // ── Thử lại ──────────────────────────────────────────────────────────────

    [Fact]
    public void CanRetry_IsTrueWhileClosed_FalseForTrialsAndOnceTheCircuitOpened()
    {
        var normal = Acquire();
        Assert.True(_circuit.CanRetry(normal));
        Assert.True(_circuit.CanRetry(TadssCircuitPermit.Untracked));

        _circuit.RecordAttempt(normal, TadssAttemptOutcome.Failure);
        _circuit.RecordProbe(healthy: false);
        Assert.False(_circuit.CanRetry(normal));

        _circuit.RecordProbe(healthy: true);
        Assert.False(_circuit.CanRetry(Acquire()));
    }

    // ── Cấu hình đổi ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SettingsChange_WhenCircuitOrFlagTurnedOff_ClosesAndStartsCleanWhenTurnedBackOn(bool circuitEnabled, bool flagEnabled)
    {
        OpenByConfirmation();

        Reconfigure(s => { s.CircuitEnabled = circuitEnabled; s.EnableTadssDecision = flagEnabled; });
        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
        Assert.False(_circuit.ShouldProbe);

        Reconfigure(_ => { });
        Fail();
        Fail();
        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
    }

    [Fact]
    public void SettingsChange_WhenOnlyOtherKeysChange_KeepsTheState()
    {
        OpenByConfirmation();

        Reconfigure(s => s.TimeoutMs = 500);

        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    // ── Phân loại ────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_CountsOnlyTimeoutNetworkAnd5xxAsFailures()
    {
        Assert.Equal(TadssAttemptOutcome.Success, TadssCircuit.Classify(TadssCallResult<string>.Success("x", 200, "c")));
        Assert.Equal(TadssAttemptOutcome.Failure, TadssCircuit.Classify(TadssCallResult<string>.Failure(TadssFailureKind.Timeout, "c")));
        Assert.Equal(TadssAttemptOutcome.Failure, TadssCircuit.Classify(TadssCallResult<string>.Failure(TadssFailureKind.Network, "c")));
        Assert.Equal(TadssAttemptOutcome.Failure, TadssCircuit.Classify(TadssCallResult<string>.Failure(TadssFailureKind.Http, "c", 503)));
        Assert.Equal(TadssAttemptOutcome.Neutral, TadssCircuit.Classify(TadssCallResult<string>.Failure(TadssFailureKind.Http, "c", 400)));
        Assert.Equal(TadssAttemptOutcome.Neutral, TadssCircuit.Classify(TadssCallResult<string>.Failure(TadssFailureKind.Http, "c", 429)));
        Assert.Equal(TadssAttemptOutcome.Neutral, TadssCircuit.Classify(TadssCallResult<string>.Failure(TadssFailureKind.InvalidResponse, "c", 200)));
    }
}
