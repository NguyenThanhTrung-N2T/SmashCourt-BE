using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// BE-IT-09 (gói 6A-5 Task 13, spec 7.11, P6-19, P6-26): job báo kết quả trên host thật với TADSS giả. Đơn có quyết định dựng qua luồng thật
/// (bật cờ lúc đặt, TADSS giả trả APPROVED / NEED_APPROVAL / REJECTED); job chạy khi cờ đã tắt. DB của bộ HTTP dùng chung nên TADSS giả trả
/// 200 cho mọi quyết định khác, và test chỉ kiểm request của đơn mình (đơn vừa kết thúc luôn đứng đầu vì chọn theo updated_at mới nhất).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Tadss")]
[Trait("Catalog", "BE-IT-09")]
public sealed class OutcomeJobFlowTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private static DateOnly Yesterday => DateTimeHelper.GetTodayInVietnam().AddDays(-1);
    private static DateTimeOffset YesterdayPlayEnd => new(Yesterday.ToDateTime(new TimeOnly(11, 0)), Vietnam);

    // ───────────────────────── Chạy job, dựng đơn ─────────────────────────

    private async Task RunAsync<TJob>(Func<TJob, Task> run) where TJob : notnull
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        await run(scope.ServiceProvider.GetRequiredService<TJob>());
    }

    private Task RunOutcomeJobAsync() => RunAsync<ITadssOutcomeJob>(job => job.ReportOutcomesAsync());

    /// <summary>Đơn online có quyết định APPROVED thu 100 % (cờ chỉ bật lúc đặt), chờ thanh toán.</summary>
    private async Task<(Order Order, Guid DecisionId)> BookWithDecisionAsync(TadssStub tadss, BookableCourt? court = null)
    {
        using (await UseTadssAsync())
        {
            tadss.ApproveAnyBooking(1m, 200_000m);
            var order = await BookPendingAsync(court);
            var decisionId = (await RequireBookingAsync(order.BookingId)).TadssDecisionId
                ?? throw new InvalidOperationException("Đơn đặt khi bật cờ phải có quyết định TADSS");
            return (order, decisionId);
        }
    }

    private async Task<(Order Order, Guid DecisionId)> CancelledByCustomerAsync(TadssStub tadss)
    {
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));
        return (order, decisionId);
    }

    private Task MovePlayToYesterdayAsync(Guid bookingId) =>
        WithDbAsync(async db =>
        {
            await db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.BookingDate, Yesterday));
            return await db.BookingCourts.Where(bc => bc.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(bc => bc.Date, Yesterday)
                .SetProperty(bc => bc.StartTime, new TimeOnly(10, 0))
                .SetProperty(bc => bc.EndTime, new TimeOnly(11, 0)));
        });

    private Task ExpirePaymentWindowAsync(Guid bookingId) =>
        WithDbAsync(async db =>
        {
            var expired = DateTime.UtcNow.AddSeconds(-1);
            await db.SlotLocks.Where(l => l.BookingId == bookingId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, expired));
            return await db.Invoices.Where(i => i.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-60))
                .SetProperty(i => i.ExpiresAt, (DateTime?)expired));
        });

    private Task SetUpdatedAtAsync(Guid bookingId, DateTime updatedAt) =>
        WithDbAsync(db => db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.UpdatedAt, updatedAt)));

    private Task<DateTime?> ReportedAtAsync(Guid bookingId) =>
        WithDbAsync(db => db.Bookings.AsNoTracking().Where(b => b.Id == bookingId).Select(b => b.TadssOutcomeReportedAt).SingleAsync());

    /// <summary>Request outcome của một quyết định; chờ WireMock ghi log (ghi sau khi trả lời).</summary>
    private static async Task<IReadOnlyList<WireMock.Logging.ILogEntry>> OutcomesAsync(TadssStub tadss, Guid decisionId, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (tadss.OutcomesFor(decisionId).Count < expected && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        if (expected == 0)
            await Task.Delay(300);
        return tadss.OutcomesFor(decisionId);
    }

    /// <summary>Đúng một request outcome cho quyết định, đúng schema của contract; trả body.</summary>
    private static async Task<JsonObject> SingleOutcomeAsync(TadssStub tadss, Guid decisionId)
    {
        var entry = Assert.Single(await OutcomesAsync(tadss, decisionId, 1));
        Assert.Equal($"{decisionId}:OUTCOME", entry.RequestMessage!.Headers!["X-Idempotency-Key"].Single());
        var body = TadssStub.OutcomeBodyOf(entry);
        Assert.True(TadssContractFiles.Evaluate(TadssContractFiles.OutcomeRequest, body.ToJsonString()).IsValid, body.ToJsonString());
        return body;
    }

    private static void AssertPlayed(JsonObject body, string outcome)
    {
        Assert.Equal(outcome, body["outcome"]!.GetValue<string>());
        var occurredAt = DateTimeOffset.Parse(body["occurredAt"]!.GetValue<string>());
        Assert.Equal(YesterdayPlayEnd, occurredAt);
        Assert.Equal(Vietnam, occurredAt.Offset);
    }

    private async Task AssertCancelledAsync(JsonObject body, string outcome, Guid bookingId)
    {
        Assert.Equal(outcome, body["outcome"]!.GetValue<string>());
        var cancelledAt = Assert.NotNull((await RequireBookingAsync(bookingId)).CancelledAt);
        Assert.Equal(cancelledAt.Ticks, DateTimeOffset.Parse(body["occurredAt"]!.GetValue<string>()).UtcTicks);
    }

    // ───────────────────────── Mỗi loại đơn gửi đúng outcome ─────────────────────────

    [Fact]
    public async Task CheckedOutOrder_SendsCompletedAtThePlayEndInVietnamTime_AndIsMarked()
    {
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        await StartPlayingAsync(order.BookingId);
        await MovePlayToYesterdayAsync(order.BookingId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));
        tadss.ReplyOutcomeForAny();
        var before = DateTime.UtcNow;

        await RunOutcomeJobAsync();

        AssertPlayed(await SingleOutcomeAsync(tadss, decisionId), "COMPLETED");
        var reportedAt = Assert.NotNull(await ReportedAtAsync(order.BookingId));
        Assert.True(reportedAt >= before.AddMilliseconds(-5));
    }

    [Fact]
    public async Task PendingPaymentOrder_SendsCompleted()
    {
        // C1: IPN để hóa đơn PARTIALLY_PAID nên đơn hết giờ chờ thu tiền tại quầy; khách đã đến
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        await StartPlayingAsync(order.BookingId);
        await MovePlayToYesterdayAsync(order.BookingId);
        await RunAsync<IBookingJob>(job => job.ProcessExpiredActiveBookingsAsync());
        Assert.Equal(BookingStatus.PENDING_PAYMENT, (await RequireBookingAsync(order.BookingId)).Status);
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        AssertPlayed(await SingleOutcomeAsync(tadss, decisionId), "COMPLETED");
    }

    [Fact]
    public async Task NoShowOrder_SendsNoShow()
    {
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        await MovePlayToYesterdayAsync(order.BookingId);
        await RunAsync<IBookingJob>(job => job.DetectNoShowBookingsAsync());
        Assert.Equal(BookingStatus.NO_SHOW, (await RequireBookingAsync(order.BookingId)).Status);
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        AssertPlayed(await SingleOutcomeAsync(tadss, decisionId), "NO_SHOW");
    }

    [Fact]
    public async Task CustomerCancellation_SendsCancelledByCustomerAtCancelledAt()
    {
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await CancelledByCustomerAsync(tadss);
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await AssertCancelledAsync(await SingleOutcomeAsync(tadss, decisionId), "CANCELLED_BY_CUSTOMER", order.BookingId);
    }

    [Fact]
    public async Task StaffCancellation_SendsCancelledByStaff()
    {
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await AssertCancelledAsync(await SingleOutcomeAsync(tadss, decisionId), "CANCELLED_BY_STAFF", order.BookingId);
    }

    [Fact]
    public async Task PaymentExpiry_SendsPaymentExpired_AndMoneyArrivingLaterDoesNotChangeIt()
    {
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        await ExpirePaymentWindowAsync(order.BookingId);
        await RunPaymentExpiryJobAsync();
        // Tiền về sau khi hủy: CANCELLED_PENDING_REFUND, lý do giữ PAYMENT_EXPIRED
        await EnsureCancelPoliciesAsync();
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, (await RequireBookingAsync(order.BookingId)).Status);
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await AssertCancelledAsync(await SingleOutcomeAsync(tadss, decisionId), "PAYMENT_EXPIRED", order.BookingId);
    }

    [Fact]
    public async Task ApprovedButNeverPaid_SendsPaymentExpired()
    {
        var tadss = await GetTadssAsync();
        PendingApproval pending;
        using (await UseTadssAsync())
        {
            pending = await CreatePendingApprovalAsync(tadss);
            var manager = await SeedManagerAsync(pending.Court.BranchId);
            tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
            await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m));
        }
        await ExpirePaymentWindowAsync(pending.BookingId);
        await RunPaymentExpiryJobAsync();
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await AssertCancelledAsync(await SingleOutcomeAsync(tadss, pending.DecisionId), "PAYMENT_EXPIRED", pending.BookingId);
    }

    [Fact]
    public async Task ApprovalExpiry_SendsApprovalExpired()
    {
        var tadss = await GetTadssAsync();
        PendingApproval pending;
        using (await UseTadssAsync())
            pending = await CreatePendingApprovalAsync(tadss);
        await MoveApprovalExpiryAsync(pending.BookingId, DateTime.UtcNow.AddSeconds(-1));
        await RunAsync<IBookingApprovalJob>(job => job.ExpireApprovalsAsync());
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await AssertCancelledAsync(await SingleOutcomeAsync(tadss, pending.DecisionId), "APPROVAL_EXPIRED", pending.BookingId);
        Assert.Empty(tadss.OverridesFor(pending.DecisionId));
    }

    [Fact]
    public async Task ManagerRejection_SendsRejected()
    {
        var tadss = await GetTadssAsync();
        PendingApproval pending;
        using (await UseTadssAsync())
        {
            pending = await CreatePendingApprovalAsync(tadss);
            var manager = await SeedManagerAsync(pending.Court.BranchId);
            tadss.ReplyOverride(pending.DecisionId, "REJECT", manager.Id.ToString(), Reason);
            await AssertApiStatusAsync(HttpStatusCode.OK, await RejectAsync(await CreateClientForAsync(manager), pending.BookingId));
        }
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await AssertCancelledAsync(await SingleOutcomeAsync(tadss, pending.DecisionId), "REJECTED", pending.BookingId);
    }

    [Fact]
    public async Task TadssRejectionAtBooking_SendsRejected()
    {
        var tadss = await GetTadssAsync();
        var court = await SeedCourtAsync();
        using (await UseTadssAsync())
        {
            tadss.RejectAnyBooking();
            var (call, _) = await BookAsync(court);
            // Khách nhận 400 BOOKING_REJECTED (không có data); đơn đã ghi CANCELLED / POLICY_REJECTED kèm quyết định
            Assert.Equal("BOOKING_REJECTED", call.Code);
        }
        var booking = await FindBookingOnCourtAsync(court.CourtId) ?? throw new InvalidOperationException("Không thấy booking");
        var bookingId = booking.Id;
        Assert.Equal(BookingCancelReason.POLICY_REJECTED, booking.CancelReason);
        var decisionId = Assert.NotNull(booking.TadssDecisionId);
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await AssertCancelledAsync(await SingleOutcomeAsync(tadss, decisionId), "REJECTED", bookingId);
    }

    // ───────────────────────── Không gửi / không gửi lại ─────────────────────────

    [Fact]
    public async Task SecondCycle_SendsNothingNewForAReportedOrder()
    {
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await CancelledByCustomerAsync(tadss);
        tadss.ReplyOutcomeForAny();
        await RunOutcomeJobAsync();
        await SingleOutcomeAsync(tadss, decisionId);
        var reportedAt = await ReportedAtAsync(order.BookingId);

        await RunOutcomeJobAsync();

        Assert.Single(await OutcomesAsync(tadss, decisionId, 0));
        Assert.Equal(reportedAt, await ReportedAtAsync(order.BookingId));
    }

    [Fact]
    public async Task EarlyCheckout_IsNotSentBeforeThePlayEnds()
    {
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        await StartPlayingAsync(order.BookingId);
        await MovePlayToHoursAheadAsync(order.BookingId, 2); // giờ kết thúc còn ở tương lai
        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(await SeedStaffAsync(order.Court.BranchId), order.BookingId));
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Empty(await OutcomesAsync(tadss, decisionId, 0));
        Assert.Null(await ReportedAtAsync(order.BookingId));
    }

    [Fact]
    public async Task OrderWithoutDecision_IsNeverSent()
    {
        var tadss = await GetTadssAsync();
        var order = await BookPendingAsync(); // cờ tắt lúc đặt: cọc 100 % dự phòng, không có quyết định
        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Null((await RequireBookingAsync(order.BookingId)).TadssDecisionId);
        Assert.Null(await ReportedAtAsync(order.BookingId));
    }

    [Fact]
    public async Task FlagTurnedOffExplicitly_OutcomeIsStillSent()
    {
        var tadss = await GetTadssAsync();
        var (_, decisionId) = await CancelledByCustomerAsync(tadss);
        var factory = await GetFactoryAsync();
        factory.SetSetting(EnableKey, "false");
        try
        {
            tadss.ReplyOutcomeForAny();

            await RunOutcomeJobAsync();

            await SingleOutcomeAsync(tadss, decisionId);
        }
        finally
        {
            factory.SetSetting(EnableKey, null);
        }
    }

    [Fact]
    public async Task TwentyFiveFinishedOrders_FirstCycleSends20_NextCycleTheRest()
    {
        var tadss = await GetTadssAsync();
        var decisions = new List<Guid>();
        for (var i = 0; i < 25; i++)
            decisions.Add((await CancelledByCustomerAsync(tadss)).DecisionId);
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();
        await Task.Delay(300);
        Assert.Equal(20, decisions.Count(d => tadss.OutcomesFor(d).Count == 1));

        await RunOutcomeJobAsync();
        await Task.Delay(300);
        Assert.All(decisions, d => Assert.Single(tadss.OutcomesFor(d)));
    }

    // ───────────────────────── Lỗi: dừng chu kỳ (C3) và gửi lại ─────────────────────────

    /// <summary>Hai đơn đã hủy; đơn mới hơn được xử lý trước trong chu kỳ.</summary>
    private async Task<((Order Order, Guid DecisionId) Older, (Order Order, Guid DecisionId) Newer)> TwoCancelledAsync(TadssStub tadss)
    {
        var older = await CancelledByCustomerAsync(tadss);
        var newer = await CancelledByCustomerAsync(tadss);
        await SetUpdatedAtAsync(older.Order.BookingId, DateTime.UtcNow.AddMinutes(-2));
        await SetUpdatedAtAsync(newer.Order.BookingId, DateTime.UtcNow.AddMinutes(-1));
        return (older, newer);
    }

    [Theory]
    [InlineData(429, "RATE_LIMITED", "2")]
    [InlineData(503, "PERSISTENCE_UNAVAILABLE", "1")]
    public async Task SystemFailure_StopsTheCycle_NextCycleSendsTheSameContent(int status, string code, string retryAfter)
    {
        var tadss = await GetTadssAsync();
        var (older, newer) = await TwoCancelledAsync(tadss);
        tadss.ReplyOutcomeProblem(newer.DecisionId, status, code, retryAfter);
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        var first = TadssStub.OutcomeBodyOf(Assert.Single(await OutcomesAsync(tadss, newer.DecisionId, 1)));
        Assert.Empty(await OutcomesAsync(tadss, older.DecisionId, 0)); // dừng chu kỳ: đơn sau không được gửi
        Assert.Null(await ReportedAtAsync(newer.Order.BookingId));

        tadss.Reset();
        tadss.ReplyOutcomeForAny();
        await RunOutcomeJobAsync();

        var second = await SingleOutcomeAsync(tadss, newer.DecisionId);
        Assert.Equal(first.ToJsonString(), second.ToJsonString()); // cùng nội dung ⇒ TADSS thật trả 200 bản đã lưu
        await SingleOutcomeAsync(tadss, older.DecisionId);
        Assert.NotNull(await ReportedAtAsync(newer.Order.BookingId));
        Assert.NotNull(await ReportedAtAsync(older.Order.BookingId));
    }

    [Fact]
    public async Task NgrokHtmlNotFound_IsASystemFailureNotAPerOrderRejection()
    {
        var tadss = await GetTadssAsync();
        var (older, newer) = await TwoCancelledAsync(tadss);
        tadss.ReplyOutcomeRaw(newer.DecisionId, 404, "<html><body>ERR_NGROK_3200 tunnel not found</body></html>", "text/html");
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Single(await OutcomesAsync(tadss, newer.DecisionId, 1));
        Assert.Empty(await OutcomesAsync(tadss, older.DecisionId, 0));
        Assert.Null(await ReportedAtAsync(newer.Order.BookingId));
    }

    [Fact]
    public async Task DecisionNotFound_IsLeftForLater_AndTheCycleContinues()
    {
        var tadss = await GetTadssAsync();
        var (older, newer) = await TwoCancelledAsync(tadss);
        tadss.ReplyOutcomeProblem(newer.DecisionId, 404, "DECISION_NOT_FOUND");
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Single(await OutcomesAsync(tadss, newer.DecisionId, 1));
        Assert.Null(await ReportedAtAsync(newer.Order.BookingId));
        await SingleOutcomeAsync(tadss, older.DecisionId);
        Assert.NotNull(await ReportedAtAsync(older.Order.BookingId));
        var factory = await GetFactoryAsync();
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("DECISION_NOT_FOUND")
            && e.Message.Contains(newer.Order.BookingId.ToString()));
    }

    [Fact]
    public async Task OkWithAnotherDecision_IsNotMarked_StopsTheCycle_AndTheNextCycleMarks()
    {
        // B4: proxy trỏ nhầm sang một TADSS khác trả 200 cho quyết định khác
        var tadss = await GetTadssAsync();
        var (older, newer) = await TwoCancelledAsync(tadss);
        var cancelledAt = Assert.NotNull((await RequireBookingAsync(newer.Order.BookingId)).CancelledAt);
        tadss.ReplyOutcomeRaw(newer.DecisionId, 200,
            TadssStub.OutcomeResponseBody(Guid.NewGuid(), "CANCELLED_BY_CUSTOMER", new DateTimeOffset(cancelledAt).ToString("O")), "application/json");
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Single(await OutcomesAsync(tadss, newer.DecisionId, 1));
        Assert.Null(await ReportedAtAsync(newer.Order.BookingId));
        Assert.Empty(await OutcomesAsync(tadss, older.DecisionId, 0));

        tadss.Reset();
        tadss.ReplyOutcomeForAny();
        await RunOutcomeJobAsync();

        Assert.NotNull(await ReportedAtAsync(newer.Order.BookingId));
    }

    // ───────────────────────── Timeout riêng 5 giây (C8) ─────────────────────────

    [Fact]
    public async Task SlowTadssWithinFiveSeconds_IsRecorded()
    {
        // DB Neon của TADSS thức dậy mất 1–3 giây; TimeoutMs của host test (400 ms) sẽ cắt nếu lệnh outcome dùng nó
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await CancelledByCustomerAsync(tadss);
        tadss.ReplyOutcome(decisionId, delay: TimeSpan.FromSeconds(2));
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        await SingleOutcomeAsync(tadss, decisionId);
        Assert.NotNull(await ReportedAtAsync(order.BookingId));
    }

    [Fact]
    public async Task TadssSlowerThanFiveSeconds_TimesOut_NextCycleMarks()
    {
        var tadss = await GetTadssAsync();
        var (older, newer) = await TwoCancelledAsync(tadss);
        tadss.ReplyOutcome(newer.DecisionId, delay: TimeSpan.FromSeconds(6));
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Null(await ReportedAtAsync(newer.Order.BookingId));
        Assert.Empty(await OutcomesAsync(tadss, older.DecisionId, 0)); // timeout là lỗi hệ thống: dừng chu kỳ

        await Task.Delay(1500); // WireMock trả xong lần chậm trước khi reset
        tadss.Reset();
        tadss.ReplyOutcomeForAny();
        await RunOutcomeJobAsync();

        Assert.NotNull(await ReportedAtAsync(newer.Order.BookingId));
        Assert.NotNull(await ReportedAtAsync(older.Order.BookingId));
    }

    // ───────────────────────── Thứ tự chọn đơn (C2) ─────────────────────────

    [Fact]
    public async Task ManyOldOrdersPermanentlyRejected_NewlyFinishedOrdersAreStillReportedNextCycle()
    {
        // Bảo đảm của D1: DB TADSS bị reset ⇒ quyết định cũ 404 vĩnh viễn; đơn mới kết thúc vẫn được báo ngay
        var tadss = await GetTadssAsync();
        var old = new List<(Order Order, Guid DecisionId)>();
        for (var i = 0; i < 25; i++)
            old.Add(await CancelledByCustomerAsync(tadss));
        foreach (var (order, _) in old)
            await SetUpdatedAtAsync(order.BookingId, DateTime.UtcNow.AddHours(-3));
        var fresh = new List<(Order Order, Guid DecisionId)>();
        for (var i = 0; i < 3; i++)
            fresh.Add(await CancelledByCustomerAsync(tadss));
        foreach (var (_, decisionId) in old)
            tadss.ReplyOutcomeProblem(decisionId, 404, "DECISION_NOT_FOUND");
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        foreach (var (order, _) in fresh)
            Assert.NotNull(await ReportedAtAsync(order.BookingId));
        foreach (var (order, _) in old)
            Assert.Null(await ReportedAtAsync(order.BookingId));
    }

    [Fact]
    public async Task LimitOfD1_AValidOldOrderWaitsBehind20NewerRejectedOrders()
    {
        // Giới hạn đã ghi ở plan (không phải lỗi): đơn tồn hợp lệ bị 20 đơn mới hơn bị từ chối vĩnh viễn chắn chỗ
        var tadss = await GetTadssAsync();
        var waiting = await CancelledByCustomerAsync(tadss);
        await SetUpdatedAtAsync(waiting.Order.BookingId, DateTime.UtcNow.AddHours(-3));
        var rejected = new List<Guid>();
        for (var i = 0; i < 20; i++)
            rejected.Add((await CancelledByCustomerAsync(tadss)).DecisionId);
        foreach (var decisionId in rejected)
            tadss.ReplyOutcomeProblem(decisionId, 404, "DECISION_NOT_FOUND");
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Empty(await OutcomesAsync(tadss, waiting.DecisionId, 0));
        Assert.Null(await ReportedAtAsync(waiting.Order.BookingId));
    }

    // ───────────────────────── Nhãn khớp dữ liệu (C1) ─────────────────────────

    [Fact]
    public async Task NoShowThatHasACheckIn_IsHeldBackWithAnError()
    {
        // Dữ liệu cũ của lỗi B6 (Job-04 đè lần check-in): trạng thái NO_SHOW nhưng có checked_in_at
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await BookWithDecisionAsync(tadss);
        await MovePlayToYesterdayAsync(order.BookingId);
        await WithDbAsync(db => db.Bookings.Where(b => b.Id == order.BookingId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, BookingStatus.NO_SHOW)
            .SetProperty(b => b.CheckedInAt, (DateTime?)DateTime.UtcNow.AddDays(-1))
            .SetProperty(b => b.UpdatedAt, DateTime.UtcNow)));
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Empty(await OutcomesAsync(tadss, decisionId, 0));
        Assert.Null(await ReportedAtAsync(order.BookingId));
        var factory = await GetFactoryAsync();
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(order.BookingId.ToString())
            && e.Message.Contains("checked_in_at"));
    }

    [Fact]
    public async Task OutcomeAlreadyRecordedWithOtherContent_IsMarkedWithAWarning()
    {
        // TADSS giữ outcome đầu (một outcome mỗi quyết định); gửi lại không đổi được gì nên đánh dấu, không thử mãi
        var tadss = await GetTadssAsync();
        var (order, decisionId) = await CancelledByCustomerAsync(tadss);
        tadss.ReplyOutcomeProblem(decisionId, 409, "OUTCOME_ALREADY_RECORDED");
        tadss.ReplyOutcomeForAny();

        await RunOutcomeJobAsync();

        Assert.Single(await OutcomesAsync(tadss, decisionId, 1));
        Assert.NotNull(await ReportedAtAsync(order.BookingId));
        var factory = await GetFactoryAsync();
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("OUTCOME_ALREADY_RECORDED")
            && e.Message.Contains(order.BookingId.ToString()));
    }
}
