using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Gói 6A-3 Task 16: hàng chờ duyệt và phân quyền của ba endpoint mới qua HTTP thật (BE-IT-05, BE-IT-11).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class ApprovalQueueTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : ApprovalTestBase(database, host)
{
    private static List<Guid> Ids(ApiCall call) =>
        call.Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("bookingId").GetGuid()).ToList();

    // ───────────────────────── Hàng chờ (BE-IT-05) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task Manager_SeesWaitingBookingsOfTheBranch_SoonestFirst_WithEveryField()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var court = await SeedCourtAsync(200_000m);
        var later = await CreatePendingApprovalAsync(tadss, court, DateTimeOffset.UtcNow.AddMinutes(25), hour: 10);
        var sooner = await CreatePendingApprovalAsync(tadss, court, DateTimeOffset.UtcNow.AddMinutes(10), hour: 12);
        var manager = await SeedManagerAsync(court.BranchId);
        var client = await CreateClientForAsync(manager);

        var call = await QueueAsync(client);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Equal("Lấy danh sách đơn chờ duyệt thành công", call.Envelope.GetProperty("message").GetString());
        Assert.Equal([sooner.BookingId, later.BookingId], Ids(call));
        Assert.Equal(2, call.Data.GetProperty("totalItems").GetInt32());
        var item = call.Data.GetProperty("items")[0];
        Assert.Equal(court.BranchId, item.GetProperty("branchId").GetGuid());
        Assert.Equal(sooner.Customer.Id, item.GetProperty("customerId").GetGuid());
        Assert.Equal(sooner.Customer.FullName, item.GetProperty("customerName").GetString());
        Assert.False(item.GetProperty("isGuest").GetBoolean());
        Assert.Equal(FutureDate().ToString("yyyy-MM-dd"), item.GetProperty("bookingDate").GetString());
        Assert.Equal("12:00:00", item.GetProperty("startTime").GetString());
        Assert.Equal("13:00:00", item.GetProperty("endTime").GetString());
        Assert.Equal(court.CourtId, item.GetProperty("courts")[0].GetProperty("courtId").GetGuid());
        Assert.Equal(200_000m, item.GetProperty("finalTotal").GetDecimal());
        Assert.False(string.IsNullOrEmpty(item.GetProperty("approvalReasonCode").GetString()));
        Assert.Equal(JsonValueKind.Array, item.GetProperty("approvalReasons").ValueKind);
        Assert.Matches(@"^\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2}$", item.GetProperty("approvalExpiresAt").GetString());
        Assert.False(item.TryGetProperty("tadssDecisionId", out var hidden1)); // không lộ trường nội bộ TADSS
        Assert.False(item.TryGetProperty("decisionId", out var hidden2));
        Assert.Empty(tadss.OverridesFor(sooner.DecisionId));     // hàng chờ không gọi TADSS
        Assert.Empty(tadss.DetailsFor(sooner.DecisionId));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task ExpiredAndResolvedBookings_LeaveTheQueue()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var court = await SeedCourtAsync(200_000m);
        var waiting = await CreatePendingApprovalAsync(tadss, court, hour: 8);
        var expired = await CreatePendingApprovalAsync(tadss, court, hour: 10);
        var approved = await CreatePendingApprovalAsync(tadss, court, hour: 12);
        await MoveApprovalExpiryAsync(expired.BookingId, DateTime.UtcNow.AddSeconds(-1));
        var manager = await SeedManagerAsync(court.BranchId);
        var client = await CreateClientForAsync(manager);
        tadss.ReplyOverride(approved.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(client, approved.BookingId, 0.3m));

        var call = await QueueAsync(client);

        Assert.Equal([waiting.BookingId], Ids(call));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task Manager_WithOwnBranchId_IsOk_WithAnotherBranchId_Is403()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var otherBranch = await SeedCourtAsync();
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        var client = await CreateClientForAsync(manager);

        var own = await QueueAsync(client, $"?branchId={pending.Court.BranchId}");
        var other = await QueueAsync(client, $"?branchId={otherBranch.BranchId}");

        Assert.Equal([pending.BookingId], Ids(own));
        await AssertApiStatusAsync(HttpStatusCode.Forbidden, other);
        Assert.Equal("FORBIDDEN", other.Code);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task Owner_SeesEveryBranchOrFiltersOne()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var first = await CreatePendingApprovalAsync(tadss);
        var second = await CreatePendingApprovalAsync(tadss);
        var owner = await SeedUserAsync(UserRole.OWNER);
        var client = await CreateClientForAsync(owner);

        var all = Ids(await QueueAsync(client, "?pageSize=50"));
        var filtered = Ids(await QueueAsync(client, $"?branchId={first.Court.BranchId}"));

        Assert.Contains(first.BookingId, all);
        Assert.Contains(second.BookingId, all);
        Assert.Equal([first.BookingId], filtered);
    }

    [Theory]
    [InlineData(BranchStatus.SUSPENDED, HttpStatusCode.BadRequest, "BAD_REQUEST")] // tạm ngưng: "Chi nhánh không khả dụng" (API doc 4.1)
    [InlineData(BranchStatus.INACTIVE, HttpStatusCode.NotFound, "NOT_FOUND")]      // xóa mềm: repository ẩn chi nhánh
    [Trait("Catalog", "BE-IT-05")]
    public async Task ManagerOfABranchNotInOperation_IsRejected(BranchStatus status, HttpStatusCode expected, string code)
    {
        var court = await SeedCourtAsync();
        var manager = await SeedManagerAsync(court.BranchId);
        await WithDbAsync(db => db.Branches.Where(b => b.Id == court.BranchId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, status)));

        var call = await QueueAsync(await CreateClientForAsync(manager));

        await AssertApiStatusAsync(expected, call);
        Assert.Equal(code, call.Code);
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=51")]
    [InlineData("?page=0")]
    [Trait("Catalog", "BE-IT-05")]
    public async Task PagingOutOfRange_Is400ValidationError(string query)
    {
        var owner = await SeedUserAsync(UserRole.OWNER);

        var call = await QueueAsync(await CreateClientForAsync(owner), query);

        await AssertApiStatusAsync(HttpStatusCode.BadRequest, call);
        Assert.Equal("VALIDATION_ERROR", call.Code);
    }

    // ───────────────────────── Phân quyền (BE-IT-11) ─────────────────────────

    public static TheoryData<string> Endpoints => new() { "queue", "approve", "reject" };

    private static Task<ApiCall> CallAsync(string endpoint, HttpClient client, Guid bookingId) => endpoint switch
    {
        "queue" => QueueAsync(client),
        "approve" => ApproveAsync(client, bookingId, 0.3m),
        _ => RejectAsync(client, bookingId)
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    [Trait("Catalog", "BE-IT-11")]
    public async Task Anonymous_Is401WithoutEnvelope(string endpoint)
    {
        var call = await CallAsync(endpoint, await CreateAnonymousClientAsync(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, call.Status);
        Assert.False(call.IsEnvelope);
    }

    [Theory]
    [InlineData("queue", UserRole.STAFF)]
    [InlineData("approve", UserRole.STAFF)]
    [InlineData("reject", UserRole.STAFF)]
    [InlineData("queue", UserRole.CUSTOMER)]
    [InlineData("approve", UserRole.CUSTOMER)]
    [InlineData("reject", UserRole.CUSTOMER)]
    [Trait("Catalog", "BE-IT-11")]
    public async Task StaffOrCustomer_Is403WithoutEnvelope_AndTadssIsNotCalled(string endpoint, UserRole role)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var user = role == UserRole.STAFF ? await SeedStaffAsync(pending.Court.BranchId) : pending.Customer;

        var call = await CallAsync(endpoint, await CreateClientForAsync(user), pending.BookingId);

        Assert.Equal(HttpStatusCode.Forbidden, call.Status);
        Assert.False(call.IsEnvelope);
        Assert.Empty(await tadss.WaitForOverridesAsync(pending.DecisionId, 0));
        Assert.Equal(BookingStatus.PENDING_APPROVAL, (await ReadApprovalBookingAsync(pending.BookingId)).Status);
    }

    [Theory]
    [InlineData("approve", false)]
    [InlineData("reject", false)]
    [InlineData("approve", true)]
    [InlineData("reject", true)]
    [Trait("Catalog", "BE-IT-11")]
    public async Task ManagerOfAnotherBranchOrWithoutBranch_Is403Envelope_AndBookingUnchanged(string endpoint, bool unassigned)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = unassigned ? await SeedUserAsync(UserRole.BRANCH_MANAGER) : await SeedManagerAsync((await SeedCourtAsync()).BranchId);

        var call = await CallAsync(endpoint, await CreateClientForAsync(manager), pending.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.Forbidden, call);
        Assert.Equal("FORBIDDEN", call.Code);
        Assert.Empty(await tadss.WaitForOverridesAsync(pending.DecisionId, 0));
        Assert.Equal(BookingStatus.PENDING_APPROVAL, (await ReadApprovalBookingAsync(pending.BookingId)).Status);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [Trait("Catalog", "BE-IT-11")]
    public async Task Owner_DecidesBookingsOfAnyBranch(string endpoint)
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var owner = await SeedUserAsync(UserRole.OWNER);
        tadss.ReplyOverride(pending.DecisionId, endpoint == "approve" ? "APPROVE" : "REJECT", owner.Id.ToString(), Reason, 0.3m, 60_000m);

        var call = await CallAsync(endpoint, await CreateClientForAsync(owner), pending.BookingId);

        await AssertApiStatusAsync(HttpStatusCode.OK, call);
        Assert.Equal(owner.Id, (await ReadApprovalBookingAsync(pending.BookingId)).ApprovalResolvedBy);
    }
}
