using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-3 Task 17: TADSS lỗi khi duyệt / từ chối qua HTTP thật (BE-IT-14, ma trận Mục 5 của API doc, ba dòng chốt 24/10).
/// Không có đường lùi cọc 100 %: đơn luôn giữ PENDING_APPROVAL, cọc trống, không Payment, slot lock không đổi.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class ApprovalTadssFailureTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : ApprovalTestBase(database, host)
{
    private async Task AssertStillWaitingAsync(PendingApproval pending, IReadOnlyList<DateTime> locksBefore)
    {
        var booking = await ReadApprovalBookingAsync(pending.BookingId);
        Assert.Equal(BookingStatus.PENDING_APPROVAL, booking.Status);
        Assert.Null(booking.ApprovalResult);
        Assert.Null(booking.Invoice!.DepositSource);
        Assert.Null(booking.Invoice.DepositAmount);
        Assert.Empty(booking.Invoice.Payments);
        Assert.Equal(locksBefore, booking.SlotLocks.Select(l => l.ExpiresAt).OrderBy(x => x).ToList());
    }

    private async Task<IReadOnlyList<DateTime>> LocksAsync(Guid bookingId) =>
        (await ReadApprovalBookingAsync(bookingId)).SlotLocks.Select(l => l.ExpiresAt).OrderBy(x => x).ToList();

    [Theory]
    [InlineData(500, "INTERNAL_ERROR")]
    [InlineData(503, "PERSISTENCE_UNAVAILABLE")]
    [InlineData(504, "GATEWAY_TIMEOUT")]
    [InlineData(429, "RATE_LIMITED")]
    [InlineData(401, "UNAUTHORIZED")]
    [InlineData(403, null)]   // chốt 24/10: lỗi cấu hình/đường đi
    [InlineData(404, null)]   // chốt 24/10: VD URL ngrok đổi
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssHttpError_Is503TadssUnavailable_AndTheBookingKeepsWaiting(int status, string? code)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var locksBefore = await LocksAsync(pending.BookingId);
        tadss.ReplyOverrideProblem(pending.DecisionId, status, code);

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.ServiceUnavailable, call);
        Assert.Equal("TADSS_UNAVAILABLE", call.Code);
        await AssertStillWaitingAsync(pending, locksBefore);
        Assert.Single(await tadss.WaitForOverridesAsync(pending.DecisionId, 1)); // không tự thử lại
    }

    [Fact]
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssTimeout_Is503_AndEveryAttemptUsesTheSameIdempotencyKey()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssWithShortTimeoutAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var locksBefore = await LocksAsync(pending.BookingId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m, delay: TimeSpan.FromSeconds(2));
        var client = await CreateClientForAsync(manager);

        var first = await ApproveAsync(client, pending.BookingId, 0.3m);
        var second = await ApproveAsync(client, pending.BookingId, 0.3m);

        Assert.Equal("TADSS_UNAVAILABLE", first.Code);
        Assert.Equal("TADSS_UNAVAILABLE", second.Code);
        await AssertStillWaitingAsync(pending, locksBefore);
        var requests = await tadss.WaitForOverridesAsync(pending.DecisionId, 2);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, r => Assert.Equal($"{pending.DecisionId}:APPROVE", r.RequestMessage!.Headers!["X-Idempotency-Key"].Single()));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssUnauthorized_IsLoggedAsError()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverrideProblem(pending.DecisionId, 401, "UNAUTHORIZED");
        var factory = await GetFactoryAsync();

        await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(pending.DecisionId.ToString()));
    }

    public static TheoryData<int, string, string> ConflictMappings => new()
    {
        { 404, "DECISION_NOT_FOUND", "APPROVAL_DECISION_NOT_FOUND" },
        { 409, "NOT_PENDING_APPROVAL", "BOOKING_NOT_PENDING_APPROVAL" },
        { 409, "APPROVAL_EXPIRED", "APPROVAL_EXPIRED" }
    };

    [Theory]
    [MemberData(nameof(ConflictMappings))]
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssStateConflict_Is409WithTheBeCode(int status, string tadssCode, string beCode)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var locksBefore = await LocksAsync(pending.BookingId);
        tadss.ReplyOverrideProblem(pending.DecisionId, status, tadssCode);

        var call = await RejectAsync(await CreateClientForAsync(manager), pending.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.Conflict, call);
        Assert.Equal(beCode, call.Code);
        await AssertStillWaitingAsync(pending, locksBefore);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssRejectsTheDepositRate_Is400OnDepositRate()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverrideProblem(pending.DecisionId, 400, "VALIDATION_ERROR",
            new JsonObject { ["deposit.rate"] = new JsonArray("deposit.rate phải nằm trong [0, 1].") });

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Equal("VALIDATION_ERROR", call.Code);
        Assert.True(call.Envelope.GetProperty("errors").TryGetProperty("DepositRate", out var _));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssMissingHeader_IsAProgrammingError500()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var locksBefore = await LocksAsync(pending.BookingId);
        tadss.ReplyOverrideProblem(pending.DecisionId, 400, "MISSING_HEADER");

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.InternalServerError, call);
        Assert.Equal("INTERNAL_ERROR", call.Code);
        await AssertStillWaitingAsync(pending, locksBefore);
    }

    [Theory]
    [InlineData("other-decision")]
    [InlineData("html")]
    [Trait("Catalog", "BE-IT-14")]
    public async Task UnusableSuccessResponse_Is503_AndNothingIsWritten(string kind)
    {
        // Chốt 24/10: response 2xx hỏng (bản của quyết định khác, trang HTML của proxy) xử lý như lỗi TADSS
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var locksBefore = await LocksAsync(pending.BookingId);
        if (kind == "html")
            tadss.ReplyOverrideRaw(pending.DecisionId, 200, "<html>ngrok</html>", "text/html");
        else
            tadss.ReplyOverrideRaw(pending.DecisionId, 200,
                TadssStub.OverrideBody(Guid.NewGuid(), "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m).ToJsonString(),
                "application/json");

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.ServiceUnavailable, call);
        Assert.Equal("TADSS_UNAVAILABLE", call.Code);
        await AssertStillWaitingAsync(pending, locksBefore);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-14")]
    public async Task DepositLargerThanTheBooking_Is503_AndNothingIsWritten()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var locksBefore = await LocksAsync(pending.BookingId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 1m, 200_001m);

        var call = await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m);

        await AssertApiStatusAsync(HttpStatusCode.ServiceUnavailable, call);
        await AssertStillWaitingAsync(pending, locksBefore);
    }
}
