using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Nền cho BE-IT của thanh toán, hủy và check-out: dựng đơn chờ thanh toán / đã cọc / đã trả đủ qua đúng API, gọi IPN có chữ ký thật,
/// và nạp một lần bảng chính sách hủy mẫu (trước 24 giờ hoàn 100 %, trước 12 giờ 50 %, trước 2 giờ 20 %).
/// </summary>
public abstract class PaymentCancellationTestBase(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : ApprovalTestBase(database, host)
{
    protected sealed record Order(Guid BookingId, User Customer, BookableCourt Court, string TransactionRef, decimal Amount);

    private static readonly SemaphoreSlim PolicyGate = new(1, 1);

    protected async Task EnsureCancelPoliciesAsync()
    {
        await PolicyGate.WaitAsync();
        try
        {
            await WithDbAsync(async db =>
            {
                var now = DateTime.UtcNow;
                foreach (var (hours, percent) in new[] { (24, 100m), (12, 50m), (2, 20m) })
                    if (!await db.CancelPolicies.AnyAsync(p => p.HoursBefore == hours))
                        db.CancelPolicies.Add(new CancelPolicy
                        {
                            Id = Guid.NewGuid(), HoursBefore = hours, RefundPercent = percent, CreatedAt = now, UpdatedAt = now
                        });
                return await db.SaveChangesAsync();
            });
        }
        finally
        {
            PolicyGate.Release();
        }
    }

    /// <summary>Đơn online chờ thanh toán (cờ TADSS tắt ⇒ thu 100 % qua VNPay), mặc định 10:00 ba ngày sau.</summary>
    protected async Task<Order> BookPendingAsync(BookableCourt? court = null, Guid? promotionId = null, User? customer = null)
    {
        court ??= await SeedCourtAsync(200_000m);
        var (call, owner) = await BookAsync(court, promotionId, customer);
        await AssertStatusAsync(HttpStatusCode.Created, call);
        Assert.Equal("PENDING", call.DataString("status"));
        var payment = Assert.Single((await RequireBookingAsync(call.BookingId)).Invoice!.Payments);
        return new Order(call.BookingId, owner, court, payment.TransactionRef!, payment.Amount);
    }

    /// <summary>Đơn đã trả đủ 200.000 đ qua VNPay.</summary>
    protected async Task<Order> BookPaidAsync(BookableCourt? court = null, Guid? promotionId = null)
    {
        var order = await BookPendingAsync(court, promotionId);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));
        Assert.Equal(BookingStatus.PAID_ONLINE, (await RequireBookingAsync(order.BookingId)).Status);
        return order;
    }

    /// <summary>Đơn đã cọc 30 % (60.000 / 200.000 đ): chờ duyệt ⇒ Quản lý duyệt 30 % ⇒ "Thanh toán cọc" ⇒ IPN.</summary>
    protected async Task<Order> BookDepositPaidAsync()
    {
        var tadss = await GetTadssAsync();
        PendingApproval pending;
        using (await UseTadssAsync())
        {
            pending = await CreatePendingApprovalAsync(tadss);
            var manager = await SeedManagerAsync(pending.Court.BranchId);
            tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
            await AssertApiStatusAsync(HttpStatusCode.OK,
                await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m));
        }
        var retry = await RetryAsync(pending.Customer, pending.BookingId);
        await AssertApiStatusAsync(HttpStatusCode.OK, retry);
        var payment = (await RequireBookingAsync(pending.BookingId)).Invoice!.Payments.Single(p => p.Status == PaymentTxStatus.PENDING);
        Assert.Equal(HttpStatusCode.OK, await IpnAsync(payment.TransactionRef!, 60_000m));
        Assert.Equal(BookingStatus.PAID_ONLINE, (await RequireBookingAsync(pending.BookingId)).Status);
        return new Order(pending.BookingId, pending.Customer, pending.Court, payment.TransactionRef!, 60_000m);
    }

    // ───────────────────────── Gọi API ─────────────────────────

    protected async Task<HttpStatusCode> IpnAsync(string transactionRef, decimal amount, bool success = true)
    {
        using var response = await (await CreateAnonymousClientAsync())
            .GetAsync($"/api/payments/vnpay/ipn?{SignedIpnQuery(transactionRef, amount, success)}");
        return response.StatusCode;
    }

    protected async Task<ApiCall> RetryAsync(User customer, Guid bookingId) =>
        await SendAsync((await CustomerClientAsync(customer)).PostAsync($"/api/me/bookings/{bookingId}/retry-payment", null));

    protected async Task<ApiCall> CustomerCancelAsync(User customer, Guid bookingId) =>
        await SendAsync((await CustomerClientAsync(customer)).PostAsync($"/api/me/bookings/{bookingId}/cancel", null));

    protected async Task<ApiCall> StaffCancelAsync(User staff, Guid bookingId) =>
        await SendAsync((await CreateClientForAsync(staff)).PostAsync($"/api/bookings/{bookingId}/cancel", null));

    protected async Task<ApiCall> ConfirmRefundAsync(User staff, Guid bookingId) =>
        await SendAsync((await CreateClientForAsync(staff)).PostAsync($"/api/bookings/{bookingId}/confirm-refund", null));

    protected async Task<ApiCall> CheckoutAsync(User staff, Guid bookingId) =>
        await SendAsync((await CreateClientForAsync(staff)).PostAsync($"/api/bookings/{bookingId}/checkout", null));

    /// <summary>Gắn một link hủy biết trước vào đơn (token gốc trong email không đọc lại được) rồi gọi xem trước hoặc hủy.</summary>
    protected async Task<string> GiveCancelLinkAsync(Guid bookingId, DateTime? expiresAtUtc = null)
    {
        var raw = $"raw-{Guid.NewGuid():N}";
        await WithDbAsync(db => db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.CancelTokenHash, CancelTokenHelper.Hash(raw))
            .SetProperty(b => b.CancelTokenExpiresAt, expiresAtUtc ?? DateTime.UtcNow.AddHours(1))
            .SetProperty(b => b.CancelTokenUsedAt, (DateTime?)null)));
        return raw;
    }

    protected async Task<ApiCall> LinkCancelAsync(string rawToken) =>
        await SendAsync((await CreateAnonymousClientAsync()).PostAsync($"/api/bookings/cancel/{rawToken}", null));

    protected async Task<ApiCall> LinkPreviewAsync(string rawToken) =>
        await SendAsync((await CreateAnonymousClientAsync()).GetAsync($"/api/bookings/cancel/{rawToken}"));

    // ───────────────────────── Chỉnh dữ liệu để giả lập thời gian ─────────────────────────

    /// <summary>Đưa đơn vào trạng thái đang chơi (bỏ qua cửa sổ check-in theo đồng hồ thật).</summary>
    protected Task StartPlayingAsync(Guid bookingId) =>
        WithDbAsync(db => db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s
            .SetProperty(b => b.Status, BookingStatus.IN_PROGRESS)
            .SetProperty(b => b.CheckedInAt, (DateTime?)DateTime.UtcNow)));

    /// <summary>Dời giờ chơi của đơn về hôm nay, bắt đầu từ vài phút trước (giờ VN), để kiểm "đã tới giờ bắt đầu".</summary>
    protected async Task MovePlayToJustStartedAsync(Guid bookingId)
    {
        var vnNow = DateTimeHelper.GetVietnamNow();
        var today = DateOnly.FromDateTime(vnNow);
        var start = new TimeOnly(vnNow.Hour, vnNow.Minute).AddMinutes(-5);
        if (start > TimeOnly.FromDateTime(vnNow))
            start = TimeOnly.MinValue; // vừa qua nửa đêm
        var end = start < new TimeOnly(23, 0) ? start.AddHours(1) : new TimeOnly(23, 59);
        await WithDbAsync(async db =>
        {
            await db.Bookings.Where(b => b.Id == bookingId).ExecuteUpdateAsync(s => s.SetProperty(b => b.BookingDate, today));
            return await db.BookingCourts.Where(bc => bc.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(bc => bc.Date, today)
                .SetProperty(bc => bc.StartTime, start)
                .SetProperty(bc => bc.EndTime, end));
        });
    }

    /// <summary>Thêm phí dịch vụ vào hóa đơn (giữ đúng công thức chk_invoices_final_total_formula).</summary>
    protected Task AddServiceFeeAsync(Guid bookingId, decimal serviceFee) =>
        WithDbAsync(db => db.Invoices.Where(i => i.BookingId == bookingId).ExecuteUpdateAsync(s => s
            .SetProperty(i => i.ServiceFee, serviceFee)
            .SetProperty(i => i.FinalTotal, i => i.CourtFee - i.LoyaltyDiscountAmount - i.PromotionDiscountAmount + serviceFee)));

    protected Task<List<Refund>> ReadRefundsAsync(Guid bookingId) =>
        WithDbAsync(db => db.Refunds.AsNoTracking().Include(r => r.Payment)
            .Where(r => r.Payment.Invoice.BookingId == bookingId).ToListAsync());
}
