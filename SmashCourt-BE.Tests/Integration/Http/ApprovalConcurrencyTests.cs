using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-3 Task 18: lặp request, chạy song song, thử lại sau 503, đồng bộ Q9, đơn đổi trong lúc chờ TADSS (BE-IT-13, BE-IT-15). Hai request
/// song song đi qua hai kết nối DB thật nên câu cổng "còn chờ và còn hạn" được kiểm trên PostgreSQL thật.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class ApprovalConcurrencyTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : ApprovalTestBase(database, host)
{
    [Fact]
    [Trait("Catalog", "BE-IT-13")]
    public async Task SecondClickAfterSuccess_Is409AlreadyResolved_WithoutASecondOverride()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        var client = await CreateClientForAsync(manager);

        var first = await ApproveAsync(client, pending.BookingId, 0.3m);
        var second = await ApproveAsync(client, pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.OK, first);
        await AssertApiStatusAsync(HttpStatusCode.Conflict, second);
        Assert.Equal("APPROVAL_ALREADY_RESOLVED", second.Code);
        Assert.Single(await tadss.WaitForOverridesAsync(pending.DecisionId, 1));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-13")]
    public async Task TwoParallelClicksOfTheSameManager_OneWinsTheGate()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        // Cùng khóa, cùng nội dung: TADSS trả cùng bản đã lưu cho cả hai (chậm một chút để hai request chồng nhau)
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m, delay: TimeSpan.FromMilliseconds(400));
        var client = await CreateClientForAsync(manager);

        var calls = await Task.WhenAll(ApproveAsync(client, pending.BookingId, 0.3m), ApproveAsync(client, pending.BookingId, 0.3m));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], calls.Select(c => c.Status).OrderBy(s => (int)s));
        Assert.Equal("APPROVAL_ALREADY_RESOLVED", calls.Single(c => c.Status == HttpStatusCode.Conflict).Code);
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(60_000m, booking.Invoice!.DepositAmount);
    }

    [Theory]
    [InlineData("IDEMPOTENCY_KEY_REUSE")]
    [InlineData("ALREADY_OVERRIDDEN")]
    [Trait("Catalog", "BE-IT-13")]
    public async Task TwoManagers_WinnerGets200_LoserGets409_AndTheWinnersDecisionStays(string loserCode)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var winner = await SeedManagerAsync(pending.Court.BranchId);
        var loser = await SeedUserAsync(UserRole.OWNER);
        var stored = TadssStub.OverrideBody(pending.DecisionId, "APPROVE", winner.Id.ToString(), Reason, 0.3m, 60_000m);
        tadss.ReplyOverrideFirstThen(pending.DecisionId, (200, stored.ToJsonString()), (409, TadssStub.ProblemBody(409, loserCode)));
        // Người thua đọc chi tiết để đồng bộ; trả chậm để chắc người thắng ghi trước
        tadss.ReplyDecisionDetail(pending.DecisionId, pending.BookingId, stored, delay: TimeSpan.FromSeconds(1));

        var first = await ApproveAsync(await CreateClientForAsync(winner), pending.BookingId, 0.3m);
        var second = await ApproveAsync(await CreateClientForAsync(loser), pending.BookingId, 0.4m);

        await AssertApiStatusAsync(HttpStatusCode.OK, first);
        await AssertApiStatusAsync(HttpStatusCode.Conflict, second);
        Assert.Equal("APPROVAL_ALREADY_RESOLVED", second.Code);
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(winner.Id, booking.ApprovalResolvedBy);
        Assert.Equal(60_000m, booking.Invoice!.DepositAmount);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-13")]
    public async Task RetryAfter503WithTheSameContent_GetsTheStoredOverride_AndSucceeds()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var stored = TadssStub.OverrideBody(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        tadss.ReplyOverrideSlowlyOnceThen(pending.DecisionId, stored, firstDelay: TimeSpan.FromSeconds(2));
        var client = await CreateClientForAsync(manager);

        var first = await ApproveAsync(client, pending.BookingId, 0.3m);
        await tadss.WaitForOverridesAsync(pending.DecisionId, 1); // TADSS đã ghi lần đầu
        var retry = await ApproveAsync(client, pending.BookingId, 0.3m);

        Assert.Equal("TADSS_UNAVAILABLE", first.Code);
        await AssertApiStatusAsync(HttpStatusCode.OK, retry);
        Assert.Equal(BookingStatus.PENDING, (await ReadApprovalBookingAsync(pending.BookingId)).Status);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-13")]
    public async Task RetryAfter503WithADifferentRate_Is409_AndTheBookingFollowsTheStoredOverride()
    {
        // Q9: TADSS đã ghi 30 %; Quản lý thử lại 40 % ⇒ khóa đã dùng với nội dung khác ⇒ BE đồng bộ theo 30 % và trả 409
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var stored = TadssStub.OverrideBody(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        tadss.ReplyOverrideSlowlyOnceThen(pending.DecisionId, stored, TimeSpan.FromSeconds(2),
            later: (409, TadssStub.ProblemBody(409, "IDEMPOTENCY_KEY_REUSE")));
        tadss.ReplyDecisionDetail(pending.DecisionId, pending.BookingId, stored);
        var client = await CreateClientForAsync(manager);
        var factory = await GetFactoryAsync();

        var first = await ApproveAsync(client, pending.BookingId, 0.3m);
        await tadss.WaitForOverridesAsync(pending.DecisionId, 1);
        factory.Emails.Clear();
        var retry = await ApproveAsync(client, pending.BookingId, 0.4m);

        Assert.Equal("TADSS_UNAVAILABLE", first.Code);
        await AssertApiStatusAsync(HttpStatusCode.Conflict, retry);
        Assert.Equal("APPROVAL_ALREADY_RESOLVED", retry.Code);
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(0.3m, booking.Invoice!.DepositRate);
        Assert.Equal(60_000m, booking.Invoice.DepositAmount);
        Assert.Equal(manager.Id, booking.ApprovalResolvedBy);
        Assert.Single(factory.Emails.To(pending.Customer.Email)); // khách được báo lần đầu tiên
    }

    public static TheoryData<string, string> ChangesDuringTheCall => new()
    {
        { "cancelled", "BOOKING_NOT_PENDING_APPROVAL" },
        { "expired", "APPROVAL_EXPIRED" },
        { "resolved", "APPROVAL_ALREADY_RESOLVED" }
    };

    [Theory]
    [MemberData(nameof(ChangesDuringTheCall))]
    [Trait("Catalog", "BE-IT-15")]
    public async Task BookingChangesWhileTadssAnswers_Is409_NeverRevivesTheBooking(string change, string code)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 5_000);
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m, delay: TimeSpan.FromSeconds(1));
        var factory = await GetFactoryAsync();

        var approving = ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);
        await Task.Delay(300); // request đang chờ TADSS
        await WithDbAsync(db => change switch
        {
            "cancelled" => db.Bookings.Where(b => b.Id == pending.BookingId).ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.CANCELLED)
                .SetProperty(b => b.CancelReason, BookingCancelReason.CUSTOMER_CANCELLED)
                .SetProperty(b => b.CancelSource, CancelSourceEnum.CUSTOMER)
                .SetProperty(b => b.CancelledAt, DateTime.UtcNow)),
            "expired" => db.Bookings.Where(b => b.Id == pending.BookingId).ExecuteUpdateAsync(s => s
                .SetProperty(b => b.ApprovalExpiresAt, DateTime.UtcNow.AddSeconds(-1))),
            _ => db.Bookings.Where(b => b.Id == pending.BookingId).ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, BookingStatus.PENDING)
                .SetProperty(b => b.ApprovalResult, ApprovalResult.APPROVED)
                .SetProperty(b => b.ApprovalResolvedAt, DateTime.UtcNow))
        });
        var call = await approving;

        await AssertApiStatusAsync(HttpStatusCode.Conflict, call);
        Assert.Equal(code, call.Code);
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Null(booking.Invoice!.DepositSource); // kết quả của TADSS không được ghi
        Assert.Empty(booking.Invoice.Payments);
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(pending.DecisionId.ToString()));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-15")]
    public async Task ApproveRightBeforeTheDeadline_TadssSlow_Is409Expired_EvenThoughTadssSaid200()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync(timeoutMs: 5_000);
        var pending = await CreatePendingApprovalAsync(tadss);
        await MoveApprovalExpiryAsync(pending.BookingId, DateTime.UtcNow.AddSeconds(2));
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m, delay: TimeSpan.FromSeconds(3));

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.Conflict, call);
        Assert.Equal("APPROVAL_EXPIRED", call.Code);
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PENDING_APPROVAL, booking.Status);
        Assert.Null(booking.Invoice!.DepositSource);
    }
}
