using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Nền cho BE-IT của luồng duyệt cọc (gói 6A-3 Phần C). Đơn chờ duyệt dựng bằng luồng đặt sân thật: bật cờ, TADSS giả trả NEED_APPROVAL,
/// gọi <c>POST /api/bookings/online</c>, đọc mã quyết định từ DB. Giá mặc định 200.000 đ/giờ, đặt 1 giờ ⇒ finalTotal 200.000 đ.
/// </summary>
public abstract class ApprovalTestBase(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : OnlineBookingTestBase(database, host)
{
    protected const string Reason = "Khach quen, da goi xac nhan";

    /// <summary>Đơn chờ duyệt vừa tạo: booking, mã quyết định TADSS, khách, sân.</summary>
    protected sealed record PendingApproval(Guid BookingId, Guid DecisionId, User Customer, BookableCourt Court);

    /// <summary>Kết quả gọi một endpoint duyệt: mã HTTP và envelope (có thể rỗng với 401/403 của lớp xác thực).</summary>
    protected sealed record ApiCall(HttpStatusCode Status, JsonElement Envelope, string Raw)
    {
        public string? Code => Envelope.ValueKind == JsonValueKind.Object && Envelope.TryGetProperty("code", out var c) ? c.GetString() : null;
        public JsonElement Data => Envelope.GetProperty("data");
        public string? DataString(string name) => Data.GetProperty(name).GetString();
        public decimal? DataDecimal(string name) =>
            Data.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Data.GetProperty(name).GetDecimal();
        public bool IsEnvelope => Envelope.ValueKind == JsonValueKind.Object && Envelope.TryGetProperty("success", out _);
    }

    /// <summary>
    /// Tạo đơn chờ duyệt qua luồng đặt sân thật. Giữ cờ TADSS bật trong phạm vi trả về (duyệt/từ chối không phụ thuộc cờ, Q7, nhưng
    /// TimeoutMs rộng cho ca TADSS trả lời).
    /// </summary>
    protected async Task<PendingApproval> CreatePendingApprovalAsync(
        TadssStub tadss, BookableCourt? court = null, DateTimeOffset? approvalExpiresAt = null, Guid? promotionId = null,
        DateOnly? date = null, int hour = 10, bool twoCourts = false)
    {
        court ??= await SeedCourtAsync(200_000m);
        tadss.RequireApprovalForAnyBooking(approvalExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(20));
        var customer = await SeedUserAsync();
        var client = await CustomerClientAsync(customer);
        var courtIds = twoCourts ? new[] { court.CourtId, await SeedSecondCourtAsync(court) } : new[] { court.CourtId };
        var call = await PostOnlineAsync(client, OnlineRequest(courtIds, date ?? FutureDate(), hour, promotionId));
        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("PENDING_APPROVAL", call.DataString("status"));
        var booking = await RequireBookingAsync(call.BookingId);
        return new PendingApproval(call.BookingId, booking.TadssDecisionId ?? throw new InvalidOperationException("Thiếu decision id"),
            customer, court);
    }

    /// <summary>Quản lý được gán chi nhánh (<c>user_branches</c> MANAGER đang hiệu lực).</summary>
    protected async Task<User> SeedManagerAsync(Guid branchId)
    {
        var manager = await SeedUserAsync(UserRole.BRANCH_MANAGER);
        await WithDbAsync(async db =>
        {
            db.UserBranches.Add(new UserBranch
            {
                Id = Guid.NewGuid(), UserId = manager.Id, BranchId = branchId, Role = UserBranchRole.MANAGER, IsActive = true,
                AssignedAt = DateTime.UtcNow
            });
            return await db.SaveChangesAsync();
        });
        return manager;
    }

    protected static async Task<ApiCall> SendAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        var text = await response.Content.ReadAsStringAsync();
        JsonElement envelope = default;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { envelope = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { }
        }
        return new ApiCall(response.StatusCode, envelope, text);
    }

    protected static Task<ApiCall> ApproveAsync(HttpClient client, Guid bookingId, decimal rate, string reason = Reason) =>
        SendAsync(client.PostAsJsonAsync($"/api/bookings/{bookingId}/approve", new { depositRate = rate, reason }));

    protected static Task<ApiCall> RejectAsync(HttpClient client, Guid bookingId, string reason = Reason) =>
        SendAsync(client.PostAsJsonAsync($"/api/bookings/{bookingId}/reject", new { reason }));

    protected static Task<ApiCall> QueueAsync(HttpClient client, string query = "") =>
        SendAsync(client.GetAsync($"/api/bookings/pending-approval{query}"));

    protected async Task AssertApiStatusAsync(HttpStatusCode expected, ApiCall call)
    {
        if (call.Status == expected)
            return;
        var factory = await GetFactoryAsync();
        var errors = factory.Logs.Entries.Where(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error).TakeLast(3);
        Assert.Fail($"Mong {expected}, nhận {call.Status}: {call.Raw}{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>Đọc lại booking kèm hóa đơn, sân, slot lock, khuyến mãi.</summary>
    protected Task<Booking> ReadApprovalBookingAsync(Guid bookingId) =>
        WithDbAsync(db => db.Bookings.AsNoTracking()
            .Include(b => b.Invoice).ThenInclude(i => i!.Payments)
            .Include(b => b.BookingCourts)
            .Include(b => b.SlotLocks)
            .SingleAsync(b => b.Id == bookingId));

    /// <summary>Chạy Job-01 (hủy đơn quá hạn thanh toán) trực tiếp, không qua Hangfire.</summary>
    protected async Task RunPaymentExpiryJobAsync()
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBookingJob>().CancelExpiredPendingBookingsAsync();
    }

    /// <summary>Lùi hạn duyệt của đơn về một mốc (giả lập thời gian trôi; TADSS và BE dùng đồng hồ thật).</summary>
    protected Task MoveApprovalExpiryAsync(Guid bookingId, DateTime approvalExpiresAtUtc) =>
        WithDbAsync(db => db.Bookings.Where(b => b.Id == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ApprovalExpiresAt, approvalExpiresAtUtc)));
}
