using SmashCourt_BE.Models.Deposit;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Task 2 của 6A-2a: TADSS giả (WireMock) nối vào host thật. <c>ITadssClient</c> và <c>IBookingDepositService</c> lấy từ DI của host nên
/// kiểm cả cấu hình, HttpClient thật và đường ống quyết định cọc trên DB thật (BE-IT-19 đổi cấu hình lúc chạy).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Tadss")]
[Trait("Catalog", "BE-UT-01")]
[Trait("Catalog", "BE-IT-04")]
[Trait("Catalog", "BE-IT-19")]
public sealed class HttpTadssIntegrationTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    private const string EnableKey = "TadssConfig:EnableTadssDecision";

    /// <summary>Bật cờ TADSS trong phạm vi một test rồi trả về trạng thái mặc định (tắt).</summary>
    private async Task<IDisposable> EnableTadssAsync()
    {
        var factory = await GetFactoryAsync();
        factory.SetSetting(EnableKey, "true");
        return new Restore(() => factory.SetSetting(EnableKey, null));
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(Guid reservationId)
    {
        var request = TadssTestData.DecisionRequest(full: true);
        request.ReservationId = reservationId;
        return InScopeAsync(sp => sp.GetRequiredService<ITadssClient>().EvaluateAsync(request, reservationId));
    }

    private async Task<TadssDecisionResultDto> ValueOfAsync(Guid reservationId)
    {
        var result = await EvaluateAsync(reservationId);
        Assert.True(result.IsSuccess, $"TADSS giả trả lỗi: {result.FailureKind} {result.HttpStatus}");
        return result.Value ?? throw new InvalidOperationException("Kết quả thành công nhưng không có giá trị.");
    }

    // ───────────────────────── Client qua DI → WireMock ─────────────────────────

    [Fact]
    public async Task Client_WhenEnabled_SendsRequiredHeadersPathAndSchemaValidBody()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = Guid.NewGuid();
        tadss.ReplyApproved(bookingId, 0.5m, 114_000m);

        var result = await EvaluateAsync(bookingId);

        Assert.True(result.IsSuccess);
        var received = Assert.Single(await tadss.WaitForEvaluationsAsync(bookingId, 1));
        Assert.Equal("POST", received.RequestMessage!.Method);
        Assert.Equal(SmashCourtWebApplicationFactory.TadssApiKey, received.RequestMessage.Headers!["X-Api-Key"].Single());
        Assert.Equal(bookingId.ToString(), received.RequestMessage.Headers["X-Idempotency-Key"].Single());
        Assert.False(string.IsNullOrWhiteSpace(received.RequestMessage.Headers["X-Correlation-ID"].Single()));
        var schema = TadssContractFiles.Evaluate(TadssContractFiles.DecisionRequest, received.RequestMessage.Body!);
        Assert.True(schema.IsValid, TadssContractFiles.Describe(schema));
    }

    [Fact]
    public async Task Client_ParsesEverySampleResponseOfTheContract()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = Guid.NewGuid();

        var approvedId = tadss.ReplyApproved(bookingId, 0.3m, 68_400m);
        var approved = await ValueOfAsync(bookingId);
        Assert.Equal((TadssDecisionStatus.APPROVED, approvedId, 68_400m), (approved.Status, approved.DecisionId, approved.Deposit!.Amount));

        var deadline = new DateTimeOffset(2026, 11, 18, 14, 15, 0, TimeSpan.FromHours(7));
        tadss.Reset();
        var needId = tadss.ReplyNeedApproval(bookingId, deadline);
        var need = await ValueOfAsync(bookingId);
        Assert.Equal((TadssDecisionStatus.NEED_APPROVAL, needId, deadline), (need.Status, need.DecisionId, need.ApprovalExpiresAt));

        tadss.Reset();
        var rejectedId = tadss.ReplyRejected(bookingId);
        var rejected = await ValueOfAsync(bookingId);
        Assert.Equal((TadssDecisionStatus.REJECTED, rejectedId), (rejected.Status, rejected.DecisionId));

        tadss.Reset();
        tadss.ReplyFallback(bookingId);
        Assert.True((await ValueOfAsync(bookingId)).FallbackUsed);
    }

    [Theory]
    [InlineData(500, 2)]
    [InlineData(503, 2)]
    [InlineData(400, 1)]
    [InlineData(401, 1)]
    [InlineData(409, 1)]
    [InlineData(413, 1)]
    [InlineData(429, 1)]
    public async Task Client_OnHttpErrors_RetriesOnlyServerErrorsOnceWithTheSameIdempotencyKey(int status, int expectedRequests)
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = Guid.NewGuid();
        tadss.Reply(status, $$"""{"code":"SOME_CODE","status":{{status}}}""");

        var result = await EvaluateAsync(bookingId);

        Assert.Equal(TadssFailureKind.Http, result.FailureKind);
        Assert.Equal(status, result.HttpStatus);
        var received = await tadss.WaitForEvaluationsAsync(bookingId, expectedRequests);
        Assert.Equal(expectedRequests, received.Count);
        Assert.All(received, e => Assert.Equal(bookingId.ToString(), e.RequestMessage!.Headers!["X-Idempotency-Key"].Single()));
    }

    [Fact]
    public async Task Client_When429CarriesRetryAfter_ExposesIt()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        tadss.Reply(429, """{"code":"RATE_LIMITED","status":429}""", retryAfter: "3");

        var result = await EvaluateAsync(Guid.NewGuid());

        Assert.Equal(TimeSpan.FromSeconds(3), result.RetryAfter);
    }

    [Fact]
    public async Task Client_WhenTadssIsSlowerThanTimeout_TimesOutAfterOneRetry()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        tadss.ReplyTooSlowly();
        var started = DateTime.UtcNow;

        var result = await EvaluateAsync(Guid.NewGuid());

        Assert.Equal(TadssFailureKind.Timeout, result.FailureKind);
        // WireMock chỉ ghi log khi trả lời xong nên không đếm request bị cắt; thời gian chứng minh có đúng hai lần chờ TimeoutMs
        // (thử lại một lần), không chờ tới 5 s của kịch bản chậm
        var elapsed = DateTime.UtcNow - started;
        Assert.InRange(elapsed, TimeSpan.FromMilliseconds(2 * SmashCourtWebApplicationFactory.TadssTimeoutMs - 100), TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData("<html>ngrok warning</html>", "text/html")]
    [InlineData("not json", "application/json")]
    [InlineData("", "application/json")]
    public async Task Client_WhenSuccessBodyIsUnusable_ReturnsInvalidResponseWithoutRetry(string body, string contentType)
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = Guid.NewGuid();
        tadss.Reply(200, body, contentType);

        var result = await EvaluateAsync(bookingId);

        Assert.Equal(TadssFailureKind.InvalidResponse, result.FailureKind);
        Assert.Single(await tadss.WaitForEvaluationsAsync(bookingId, 1));
    }

    // ───────────────────────── Đổi cấu hình lúc chạy (BE-IT-19) ─────────────────────────

    [Fact]
    public async Task Flag_WhenToggledAtRuntime_IsVisibleThroughOptionsMonitorWithinTwoSeconds()
    {
        var factory = await GetFactoryAsync();
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<TadssSettings>>();
        Assert.False(monitor.CurrentValue.EnableTadssDecision);

        factory.SetSetting(EnableKey, "true");
        try
        {
            await WaitUntilAsync(() => monitor.CurrentValue.EnableTadssDecision);
            Assert.True(monitor.CurrentValue.EnableTadssDecision);
        }
        finally
        {
            factory.SetSetting(EnableKey, null);
        }

        await WaitUntilAsync(() => !monitor.CurrentValue.EnableTadssDecision);
        Assert.False(monitor.CurrentValue.EnableTadssDecision);
    }

    [Fact]
    public async Task Flag_WhenTurnedOffAfterBeingOn_StopsCallingTadssWithoutRestart()
    {
        var tadss = await GetTadssAsync();
        var bookingId = await SeedOrderAsync(200_000m);
        tadss.ReplyApproved(bookingId, 0.5m, 100_000m);

        using (await EnableTadssAsync())
        {
            var on = await DecideAsync(bookingId, 200_000m);
            Assert.Equal(DepositBranch.TadssDeposit, on.Branch);
            Assert.Single(await tadss.WaitForEvaluationsAsync(bookingId, 1));
        }

        await WaitUntilAsync(() => !Monitor().CurrentValue.EnableTadssDecision);
        var off = await DecideAsync(bookingId, 200_000m);
        Assert.Equal(DepositBranch.FullPaymentFallback, off.Branch);
        Assert.Single(await tadss.WaitForEvaluationsAsync(bookingId, 1)); // không có request mới
    }

    [Fact]
    public async Task Flag_WhenOffByDefault_NeverCallsTadssAndFallsBackToFullPayment()
    {
        var tadss = await GetTadssAsync();
        var bookingId = await SeedOrderAsync(200_000m);
        tadss.ReplyApproved(bookingId, 0.5m, 100_000m);

        var decision = await DecideAsync(bookingId, 200_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(200_000m, decision.DepositAmount);
        Assert.Empty(await tadss.WaitForEvaluationsAsync(bookingId, 0));
    }

    private IOptionsMonitor<TadssSettings> Monitor() =>
        GetFactoryAsync().GetAwaiter().GetResult().Services.GetRequiredService<IOptionsMonitor<TadssSettings>>();

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    // ───────────────────────── Đường ống quyết định cọc trên DB thật ─────────────────────────

    // Giờ chơi xa trong tương lai: các ca dưới không xét hạn duyệt (P6-24)
    private Task<SmashCourt_BE.Models.Deposit.DepositDecision> DecideAsync(Guid bookingId, decimal finalTotal) =>
        InScopeAsync(sp => sp.GetRequiredService<IBookingDepositService>().DecideAsync(bookingId, finalTotal, DateTime.UtcNow.AddDays(30)));

    /// <summary>Đơn PENDING như transaction 1 để lại (khách, sân, hóa đơn, một dòng sân), commit thật trong DB của bộ HTTP.</summary>
    private Task<Guid> SeedOrderAsync(decimal finalTotal) =>
        WithDbAsync(async db =>
        {
            var seeder = new SmashCourt_BE.Tests.Integration.TestDataSeeder(db);
            var seed = await seeder.SeedBranchWithCourtAsync();
            var customer = await seeder.SeedCustomerAsync();
            var createdAt = DateTime.UtcNow;
            var booking = SmashCourt_BE.Tests.Integration.TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, createdAt);
            db.Bookings.Add(booking);
            var invoice = SmashCourt_BE.Tests.Integration.TestDataSeeder.NewInvoice(booking, finalTotal);
            invoice.ExpiresAt = createdAt.AddMinutes(10);
            invoice.HoldDeadline = createdAt.AddMinutes(15); // transaction 1 luôn ghi (SC6 Phụ-2)
            db.Invoices.Add(invoice);
            db.BookingCourts.Add(SmashCourt_BE.Tests.Integration.TestDataSeeder.NewBookingCourt(
                booking.Id, seed.Court.Id, DateOnly.FromDateTime(createdAt.AddDays(3)), new TimeOnly(18, 0), new TimeOnly(19, 0)));
            await db.SaveChangesAsync();
            return booking.Id;
        });

    [Fact]
    public async Task DecideAsync_ApprovedWithDeposit_ReturnsTadssDepositFromTheWire()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = await SeedOrderAsync(200_000m);
        var decisionId = tadss.ReplyApproved(bookingId, 0.3m, 60_000m);

        var decision = await DecideAsync(bookingId, 200_000m);

        Assert.Equal(DepositBranch.TadssDeposit, decision.Branch);
        Assert.Equal(decisionId, decision.TadssDecisionId);
        Assert.Equal(60_000m, decision.DepositAmount);
        Assert.Equal(0.3m, decision.DepositRate);
        var sent = Assert.Single(await tadss.WaitForEvaluationsAsync(bookingId, 1));
        Assert.Equal(bookingId.ToString(), sent.RequestMessage!.Headers!["X-Idempotency-Key"].Single());
        Assert.Contains($"\"totalPrice\":200000", sent.RequestMessage.Body);
    }

    [Fact]
    public async Task DecideAsync_NeedApproval_ReturnsDeadlineInUtc()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = await SeedOrderAsync(3_000_000m);
        // Hạn tương đối (trước đây cố định 18/11/2026): từ 6A-5 hạn đã qua lúc nhận thì đi dự phòng (P6-24)
        var deadlineUtc = DateTime.UtcNow.AddMinutes(20);
        deadlineUtc = new DateTime(deadlineUtc.Year, deadlineUtc.Month, deadlineUtc.Day, deadlineUtc.Hour, deadlineUtc.Minute, 0, DateTimeKind.Utc);
        tadss.ReplyNeedApproval(bookingId, new DateTimeOffset(deadlineUtc).ToOffset(TimeSpan.FromHours(7)));

        var decision = await DecideAsync(bookingId, 3_000_000m);

        Assert.Equal(DepositBranch.NeedApproval, decision.Branch);
        Assert.Equal(deadlineUtc, decision.ApprovalExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, decision.ApprovalExpiresAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task DecideAsync_Rejected_ReturnsRejectedEvenForAZeroOrder()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = await SeedOrderAsync(0m);
        tadss.ReplyRejected(bookingId);

        var decision = await DecideAsync(bookingId, 0m);

        Assert.Equal(DepositBranch.Rejected, decision.Branch);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(409)]
    public async Task DecideAsync_WhenTadssFails_FallsBackToFullPayment(int status)
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = await SeedOrderAsync(200_000m);
        tadss.Reply(status, $$"""{"code":"X","status":{{status}}}""");

        var decision = await DecideAsync(bookingId, 200_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Equal(200_000m, decision.DepositAmount);
        Assert.Null(decision.TadssDecisionId);
    }

    [Fact]
    public async Task DecideAsync_WhenTadssTimesOutOnASmallOrder_ConfirmsWithoutOnlinePayment()
    {
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = await SeedOrderAsync(3_000m);
        tadss.ReplyTooSlowly();

        var decision = await DecideAsync(bookingId, 3_000m);

        Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
    }

    [Fact]
    public async Task DecideAsync_WhenTadssDepositExceedsTheOrder_FallsBackInsteadOfOvercharging()
    {
        // F1 qua toàn bộ đường ống thật
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var bookingId = await SeedOrderAsync(200_000m);
        tadss.ReplyApproved(bookingId, 1m, 250_000m);

        var decision = await DecideAsync(bookingId, 200_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(200_000m, decision.DepositAmount);
    }

    [Fact]
    public async Task DecideAsync_WhenBookingDoesNotExist_FallsBackWithoutCallingTadss()
    {
        // F2 qua toàn bộ đường ống thật
        var tadss = await GetTadssAsync();
        using var _ = await EnableTadssAsync();
        var missing = Guid.NewGuid();
        tadss.ReplyApproved(missing, 0.5m, 100_000m);

        var decision = await DecideAsync(missing, 200_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Empty(await tadss.WaitForEvaluationsAsync(missing, 0));
    }
}
