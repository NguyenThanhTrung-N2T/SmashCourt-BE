using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;
using TestDataSeeder = SmashCourt_BE.Tests.Integration.TestDataSeeder;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// BE-IT-27 (P6-57): mạch ngắt lệnh đánh giá cọc trên host thật, TADSS giả (WireMock) có cả <c>/health/ready</c>. TADSS treo thì mạch mở và
/// đơn sau đi phương án dự phòng ngay, không gửi request; TADSS sống lại thì mạch tự đóng sau một đơn thử. Hai ca lệch giữa kiểm sức khỏe và
/// đường quyết định. Tham số rút ngắn (kiểm 1 s, mở 2…4 s) để test nhanh; mỗi test tắt mạch lại khi xong (mạch về đóng khi cấu hình đổi).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Tadss")]
[Trait("Catalog", "BE-IT-27")]
public sealed class HttpTadssCircuitTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    // Đặt theo thứ tự, khôi phục theo thứ tự ngược: mỗi bước phải hợp lệ với validator (Break đặt trước MaxBreak, MaxBreak gỡ trước Break)
    private static readonly (string Key, string Value)[] CircuitSettings =
    [
        ("TadssConfig:EnableTadssDecision", "true"),
        ("TadssConfig:CircuitEnabled", "true"),
        ("TadssConfig:CircuitProbeIntervalSeconds", "1"),
        ("TadssConfig:CircuitProbeTimeoutMs", "300"),
        ("TadssConfig:CircuitBreakSeconds", "2"),
        ("TadssConfig:CircuitMaxBreakSeconds", "4")
    ];

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private async Task<(TadssCircuit Circuit, IDisposable Restore)> EnableCircuitAsync()
    {
        var factory = await GetFactoryAsync();
        foreach (var (key, value) in CircuitSettings)
            factory.SetSetting(key, value);
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<TadssSettings>>();
        await EventuallyAsync(() => monitor.CurrentValue.CircuitEnabled && monitor.CurrentValue.CircuitBreakSeconds == 2, "cấu hình mạch phải có hiệu lực");
        var circuit = factory.Services.GetRequiredService<TadssCircuit>();
        Assert.Equal(TadssCircuitState.Closed, circuit.State);
        return (circuit, new Restore(() =>
        {
            foreach (var (key, _) in CircuitSettings.Reverse())
                factory.SetSetting(key, null);
        }));
    }

    private static async Task EventuallyAsync(Func<bool> condition, string because, int seconds = 6)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(condition(), because);
    }

    private async Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(Guid reservationId)
    {
        var request = TadssTestData.DecisionRequest(full: true);
        request.ReservationId = reservationId;
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITadssClient>().EvaluateAsync(request, reservationId);
    }

    [Fact]
    public async Task Circuit_WhenTadssHangs_OpensAndLaterBookingsFallBackWithoutCallingTadss_ThenClosesWhenTadssRecovers()
    {
        var tadss = await GetTadssAsync();
        var (circuit, restore) = await EnableCircuitAsync();
        using var _ = restore;
        tadss.ReplyTooSlowly();
        tadss.ReplyHealthReady(200, TimeSpan.FromSeconds(3));

        var first = await EvaluateAsync(Guid.NewGuid());
        Assert.Equal(TadssFailureKind.Timeout, first.FailureKind);
        await EventuallyAsync(() => circuit.State == TadssCircuitState.Open, "kiểm xác nhận lỗi phải mở mạch");

        var second = Guid.NewGuid();
        var watch = Stopwatch.StartNew();
        var shortCircuited = await EvaluateAsync(second);
        watch.Stop();
        Assert.Equal(TadssFailureKind.CircuitOpen, shortCircuited.FailureKind);
        Assert.True(watch.ElapsedMilliseconds < 200, $"đơn bị ngắt phải trả ngay, mất {watch.ElapsedMilliseconds} ms");
        Assert.Empty(await tadss.WaitForEvaluationsAsync(second, 0));

        tadss.Reset();
        tadss.ApproveAnyBooking(0.5m, 100_000m);
        tadss.ReplyHealthReady(200);
        await EventuallyAsync(() => circuit.State == TadssCircuitState.HalfOpen, "kiểm chuyển từ lỗi sang tốt phải cho đơn thử");

        var trial = await EvaluateAsync(Guid.NewGuid());
        Assert.True(trial.IsSuccess);
        Assert.Equal(TadssCircuitState.Closed, circuit.State);
        Assert.True((await EvaluateAsync(Guid.NewGuid())).IsSuccess);
    }

    [Fact]
    public async Task Circuit_WhenHealthIsFineButDecisionsHang_OpensAfterThreeFailedCallsAndOnlyTriesOnSchedule()
    {
        var tadss = await GetTadssAsync();
        var (circuit, restore) = await EnableCircuitAsync();
        using var _ = restore;
        // Chậm hơn TimeoutMs của host (400 ms) nhưng ngắn để WireMock (ghi log sau khi trả lời) kịp ghi các request cần đếm
        tadss.Reply(200, "{}", "application/json", delay: TimeSpan.FromMilliseconds(700));
        tadss.ReplyHealthReady(200);

        var first = Guid.NewGuid();
        await EvaluateAsync(first);
        Assert.Equal(2, (await tadss.WaitForEvaluationsAsync(first, 2)).Count);
        Assert.Equal(TadssCircuitState.Closed, circuit.State);

        // Lần gọi thứ ba lỗi thì mạch mở: đơn thứ hai bỏ lần thử lại
        var second = Guid.NewGuid();
        await EvaluateAsync(second);
        Assert.Equal(TadssCircuitState.Open, circuit.State);
        Assert.Single(await tadss.WaitForEvaluationsAsync(second, 1));

        // Kiểm vẫn tốt: không có lần chuyển lỗi → tốt nên không thử sớm; tới mốc 2 s mới nửa mở
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        Assert.Equal(TadssCircuitState.Open, circuit.State);
        await EventuallyAsync(() => circuit.State == TadssCircuitState.HalfOpen, "tới mốc lịch phải cho đơn thử");

        var trial = Guid.NewGuid();
        await EvaluateAsync(trial);
        Assert.Single(await tadss.WaitForEvaluationsAsync(trial, 1));
        Assert.Equal(TadssCircuitState.Open, circuit.State);
    }

    [Fact]
    public async Task Circuit_WhenHealthHangsButDecisionsWork_StaysClosed()
    {
        var tadss = await GetTadssAsync();
        var (circuit, restore) = await EnableCircuitAsync();
        using var _ = restore;
        tadss.ReplyHealthReady(200, TimeSpan.FromSeconds(3));
        // Lần gọi đầu timeout rồi lần thử lại thành công: chuỗi lỗi về 0 trước khi kiểm xác nhận (treo) trả kết quả
        tadss.ReplyTooSlowlyOnceThen("decision-result.approved.json", TimeSpan.FromSeconds(5));

        Assert.True((await EvaluateAsync(Guid.NewGuid())).IsSuccess);
        await Task.Delay(TimeSpan.FromSeconds(0.6));

        for (var i = 0; i < 3; i++)
            Assert.True((await EvaluateAsync(Guid.NewGuid())).IsSuccess);
        Assert.Equal(TadssCircuitState.Closed, circuit.State);
    }

    [Fact]
    public async Task DepositDecision_WhenCircuitOpen_FallsBackToFullPaymentWithoutCallingTadss()
    {
        var tadss = await GetTadssAsync();
        var (circuit, restore) = await EnableCircuitAsync();
        using var _ = restore;
        tadss.ReplyTooSlowly();
        tadss.ReplyHealthReady(503);
        await EvaluateAsync(Guid.NewGuid());
        await EventuallyAsync(() => circuit.State == TadssCircuitState.Open, "mạch phải mở");
        var bookingId = await SeedOrderAsync(200_000m);

        var decision = await InScopeDecideAsync(bookingId, 200_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Empty(await tadss.WaitForEvaluationsAsync(bookingId, 0));
    }

    private async Task<DepositDecision> InScopeDecideAsync(Guid bookingId, decimal finalTotal)
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBookingDepositService>()
            .DecideAsync(bookingId, finalTotal, DateTime.UtcNow.AddDays(30));
    }

    // Đơn PENDING như transaction 1 để lại (như HttpTadssIntegrationTests)
    private Task<Guid> SeedOrderAsync(decimal finalTotal) =>
        WithDbAsync(async db =>
        {
            var seeder = new TestDataSeeder(db);
            var seed = await seeder.SeedBranchWithCourtAsync();
            var customer = await seeder.SeedCustomerAsync();
            var createdAt = DateTime.UtcNow;
            var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, createdAt);
            db.Bookings.Add(booking);
            var invoice = TestDataSeeder.NewInvoice(booking, finalTotal);
            invoice.ExpiresAt = createdAt.AddMinutes(10);
            invoice.HoldDeadline = createdAt.AddMinutes(15);
            db.Invoices.Add(invoice);
            db.BookingCourts.Add(TestDataSeeder.NewBookingCourt(
                booking.Id, seed.Court.Id, DateOnly.FromDateTime(createdAt.AddDays(3)), new TimeOnly(18, 0), new TimeOnly(19, 0)));
            await db.SaveChangesAsync();
            return booking.Id;
        });
}
