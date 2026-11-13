using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// BE-UT-14 (P6-57): <see cref="TadssClient.EvaluateAsync"/> đi qua mạch ngắt. Mạch mở thì không gửi request nào; đơn thử chỉ gọi một lần;
/// mạch vừa mở trong lúc đơn đang chờ thì bỏ lần thử lại; đơn thử bị hủy thì trả lượt thử. Lệnh duyệt không đi qua mạch.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-14")]
public sealed class TadssClientCircuitTests : IDisposable
{
    private static string ApprovedJson => TadssContractFiles.Sample("decision-result.approved.json");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
    private readonly TestOptionsMonitor<TadssSettings> _settings = new(TadssCircuitFactory.EnabledSettings());
    private readonly TadssCircuit _circuit;

    public TadssClientCircuitTests() => _circuit = TadssCircuitFactory.Create(_settings, _time);

    public void Dispose() => _circuit.Dispose();

    private TadssClient Client(FakeHttpMessageHandler handler) => new(
        new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, _settings, new HttpContextAccessor(), TimeProvider.System,
        new ListLogger<TadssClient>(), _circuit);

    private static Task<TadssCallResult<TadssDecisionResultDto>> Evaluate(TadssClient client, CancellationToken token = default) =>
        client.EvaluateAsync(TadssTestData.DecisionRequest(full: true), TadssTestData.BookingId, token);

    private void OpenByConfirmation()
    {
        _circuit.RecordAttempt(TadssCircuitFactory.Acquire(_circuit), TadssAttemptOutcome.Failure);
        _circuit.RecordProbe(healthy: false);
        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCircuitOpen_ReturnsCircuitOpenWithoutSendingAnyRequest()
    {
        OpenByConfirmation();
        var handler = FakeHttpMessageHandler.Always(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));

        var result = await Evaluate(Client(handler));

        Assert.Equal(TadssFailureKind.CircuitOpen, result.FailureKind);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task EvaluateAsync_WhenTrialFails_CallsOnlyOnceAndReopens()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);
        var handler = FakeHttpMessageHandler.Always(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await Evaluate(Client(handler));

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Single(handler.Requests);
        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    [Fact]
    public async Task EvaluateAsync_WhenTrialSucceeds_ClosesTheCircuit()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);
        var handler = FakeHttpMessageHandler.Always(() => FakeHttpMessageHandler.Json(HttpStatusCode.OK, ApprovedJson));

        var result = await Evaluate(Client(handler));

        Assert.True(result.IsSuccess);
        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCircuitOpensDuringFirstAttempt_SkipsTheRetry()
    {
        _settings.Set(WithThreshold(1));
        var handler = FakeHttpMessageHandler.Always(() => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var result = await Evaluate(Client(handler));

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Single(handler.Requests);
        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    [Fact]
    public async Task EvaluateAsync_WhenClosed_RetriesOnceAsBefore()
    {
        var handler = FakeHttpMessageHandler.Always(() => new HttpResponseMessage(HttpStatusCode.BadGateway));

        await Evaluate(Client(handler));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(TadssCircuitState.Closed, _circuit.State);
    }

    [Fact]
    public async Task EvaluateAsync_WhenTrialIsCancelledByCaller_ReleasesTheTrial()
    {
        OpenByConfirmation();
        _circuit.RecordProbe(healthy: true);
        using var cancel = new CancellationTokenSource();
        var handler = new FakeHttpMessageHandler(async (_, _) =>
        {
            await cancel.CancelAsync();
            cancel.Token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Evaluate(Client(handler), cancel.Token));

        Assert.Equal(TadssCircuitState.HalfOpen, _circuit.State);
        Assert.True(TadssCircuitFactory.Acquire(_circuit).IsTrial);
    }

    [Fact]
    public async Task OverrideAsync_WhenCircuitOpen_StillCallsTadss()
    {
        OpenByConfirmation();
        var handler = FakeHttpMessageHandler.Always(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await Client(handler).OverrideAsync(TadssTestData.DecisionId, TadssTestData.OverrideApprove(), "manager-1");

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Single(handler.Requests);
        Assert.Equal(TadssCircuitState.Open, _circuit.State);
    }

    private static TadssSettings WithThreshold(int threshold)
    {
        var settings = TadssCircuitFactory.EnabledSettings();
        settings.CircuitFailureThreshold = threshold;
        return settings;
    }
}
