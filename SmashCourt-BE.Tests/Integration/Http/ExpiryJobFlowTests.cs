using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// BE-IT-08 và Job-01 trên host thật (gói 6A-5 Task 11): job hết hạn duyệt (APPROVAL_EXPIRED, không bao giờ tự duyệt), Job-01 qua dịch vụ hủy
/// chung (B1 lý do, C7 trạng thái booking / hóa đơn / Payment, tiền về sau khi hủy), slot lock và khoảng chết (C5). Đơn dựng qua luồng thật;
/// thời gian trôi giả lập bằng cách lùi hạn trong DB (BE và TADSS dùng đồng hồ thật).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
public sealed class ExpiryJobFlowTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private async Task RunApprovalExpiryJobAsync()
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBookingApprovalJob>().ExpireApprovalsAsync();
    }

    /// <summary>Lùi hạn thanh toán của hóa đơn và slot lock (luôn bằng nhau) về quá khứ; created_at lùi theo (chk_invoices_expires_at).</summary>
    private Task ExpirePaymentWindowAsync(Guid bookingId) =>
        WithDbAsync(async db =>
        {
            var expired = DateTime.UtcNow.AddSeconds(-1);
            await db.SlotLocks.Where(l => l.BookingId == bookingId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, expired));
            return await db.Invoices.Where(i => i.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-60))
                .SetProperty(i => i.ExpiresAt, (DateTime?)expired));
        });

    private async Task<string> RegisterWaiterAsync(BookableCourt court)
    {
        var waiter = $"cho-slot-{Guid.NewGuid():N}@test.local";
        await WithDbAsync(async db =>
        {
            db.SlotInterests.Add(new SlotInterest
            {
                Id = Guid.NewGuid(), CourtId = court.CourtId, Date = FutureDate(), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
                Email = waiter, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            return await db.SaveChangesAsync();
        });
        return waiter;
    }

    // ───────────────────────── Hết hạn duyệt (BE-IT-08) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-08")]
    public async Task ApprovalExpiry_CancelsReleasesAndEmails_NeverApproves()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var promotion = await SeedPromotionAsync(DiscountTypeEnum.PERCENT, 10m);
        var expired = await CreatePendingApprovalAsync(tadss, promotionId: promotion.Id);
        var stillValid = await CreatePendingApprovalAsync(tadss);
        var waiter = await RegisterWaiterAsync(expired.Court);
        var usedBefore = await ReadUsedCountAsync(promotion.Id);
        await MoveApprovalExpiryAsync(expired.BookingId, DateTime.UtcNow.AddSeconds(-1));
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();
        var before = DateTime.UtcNow;

        await RunApprovalExpiryJobAsync();

        var after = DateTime.UtcNow;
        var booking = await ReadApprovalBookingAsync(expired.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(CancelSourceEnum.SYSTEM, booking.CancelSource);
        Assert.Equal(BookingCancelReason.APPROVAL_EXPIRED, booking.CancelReason);
        Assert.Null(booking.CancelledBy);
        var cancelledAt = Assert.NotNull(booking.CancelledAt);
        Assert.InRange(cancelledAt, before.AddMilliseconds(-5), after.AddMilliseconds(5));
        Assert.Equal(DateTimeKind.Utc, cancelledAt.Kind);
        Assert.Empty(booking.SlotLocks);
        Assert.All(booking.BookingCourts, c => Assert.False(c.IsActive));
        Assert.Equal(InvoicePaymentStatus.EXPIRED, booking.Invoice!.PaymentStatus);
        Assert.Null(booking.ApprovalResult); // không ai duyệt
        Assert.Equal(usedBefore, await ReadUsedCountAsync(promotion.Id));

        var email = Assert.Single(factory.Emails.To(expired.Customer.Email));
        Assert.Contains("hết hạn duyệt", email.Subject);
        var waiterEmail = Assert.Single(factory.Emails.To(waiter));
        Assert.Contains("href=\"http://localhost:3000\"", waiterEmail.Body); // B3: trang chủ, không /booking?
        Assert.Empty(tadss.AllOverrides); // không bao giờ tự duyệt

        // Đơn còn hạn trong cùng lần chạy không đổi
        Assert.Equal(BookingStatus.PENDING_APPROVAL, (await RequireBookingAsync(stillValid.BookingId)).Status);

        // Rời hàng chờ; Quản lý bấm duyệt sau đó nhận 409, không gọi TADSS
        var manager = await SeedManagerAsync(expired.Court.BranchId);
        var managerClient = await CreateClientForAsync(manager);
        var queue = await QueueAsync(managerClient, $"?branchId={expired.Court.BranchId}");
        Assert.DoesNotContain(expired.BookingId.ToString(), queue.Raw);
        var approve = await ApproveAsync(managerClient, expired.BookingId, 0.3m);
        Assert.Equal(HttpStatusCode.Conflict, approve.Status);
        Assert.Equal("APPROVAL_EXPIRED", approve.Code);
        await Task.Delay(300);
        Assert.Empty(tadss.OverridesFor(expired.DecisionId));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-08")]
    public async Task ApprovalExpiry_RunTwice_SecondRunChangesNothing()
    {
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var expired = await CreatePendingApprovalAsync(tadss);
        await MoveApprovalExpiryAsync(expired.BookingId, DateTime.UtcNow.AddSeconds(-1));
        await RunApprovalExpiryJobAsync();
        var first = await RequireBookingAsync(expired.BookingId);
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();

        await RunApprovalExpiryJobAsync();

        var second = await RequireBookingAsync(expired.BookingId);
        Assert.Equal(first.CancelledAt, second.CancelledAt);
        Assert.Equal(first.UpdatedAt, second.UpdatedAt);
        Assert.Empty(factory.Emails.To(expired.Customer.Email));
    }

    // ───────────────────────── Job-01: lý do (B1), trạng thái (C7) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task PaymentExpiry_PendingLinkOrder_ExpiresEverythingAndSendsNoCustomerEmail()
    {
        var order = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId);
        var factory = await GetFactoryAsync();
        factory.Emails.Clear();

        await RunPaymentExpiryJobAsync();

        var booking = await ReadApprovalBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(CancelSourceEnum.SYSTEM, booking.CancelSource);
        Assert.Equal(BookingCancelReason.PAYMENT_EXPIRED, booking.CancelReason);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, booking.Invoice!.PaymentStatus);
        Assert.Equal(PaymentTxStatus.FAILED, Assert.Single(booking.Invoice.Payments).Status);
        Assert.Empty(booking.SlotLocks);
        Assert.All(booking.BookingCourts, c => Assert.False(c.IsActive));
        Assert.Empty(await ReadRefundsAsync(order.BookingId));
        Assert.Empty(factory.Emails.To(order.Customer.Email)); // như Job-01 cũ: không email khách
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task PaymentExpiry_OrderLeftBetweenTheTwoTransactions_IsSystemCleanup()
    {
        // BE dừng giữa transaction 1 và 2: chưa có mức cọc, chưa có Payment
        var order = await BookPendingAsync();
        await WithDbAsync(async db =>
        {
            await db.Payments.Where(p => p.Invoice.BookingId == order.BookingId).ExecuteDeleteAsync();
            return await db.Invoices.Where(i => i.BookingId == order.BookingId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.DepositSource, (DepositSource?)null)
                .SetProperty(i => i.DepositRate, (decimal?)null)
                .SetProperty(i => i.DepositAmount, (decimal?)null));
        });
        await ExpirePaymentWindowAsync(order.BookingId);

        await RunPaymentExpiryJobAsync();

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(BookingCancelReason.SYSTEM_CLEANUP, booking.CancelReason);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-21")]
    public async Task PaymentExpiry_ApprovedButNeverPaid_IsCountedAgainstTheCustomerNextTime()
    {
        // B1: đã duyệt cọc nhưng khách chưa bấm "Thanh toán cọc" (chưa có Payment) vẫn là lỗi của khách
        var tadss = await GetTadssAsync();
        using var _ = await UseTadssAsync();
        var pending = await CreatePendingApprovalAsync(tadss);
        var manager = await SeedManagerAsync(pending.Court.BranchId);
        tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m));
        await ExpirePaymentWindowAsync(pending.BookingId);

        await RunPaymentExpiryJobAsync();

        var booking = await RequireBookingAsync(pending.BookingId);
        Assert.Equal(BookingCancelReason.PAYMENT_EXPIRED, booking.CancelReason);
        Assert.Empty(booking.Invoice!.Payments);

        // Lần đặt sau của chính khách đó gửi TADSS paymentExpiredCount = 1
        tadss.Reset();
        tadss.ApproveAnyBooking(0.3m, 60_000m);
        var (next, _) = await BookAsync(pending.Court, customer: pending.Customer, hour: 14);
        await AssertStatusAsync(HttpStatusCode.Created, next);
        var request = Assert.Single(await tadss.WaitForEvaluationsAsync(next.BookingId, 1));
        var history = JsonNode.Parse(request.RequestMessage!.Body!)!["customer"]!["history"]!;
        Assert.Equal(1, history["paymentExpiredCount"]!.GetValue<int>());
    }

    [Fact]
    [Trait("Catalog", "BE-IT-22")]
    public async Task PaymentExpiry_ThenMoneyArrives_RefundsInFullAndStaffConfirms()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId);
        await RunPaymentExpiryJobAsync();
        Assert.Equal(PaymentTxStatus.FAILED, Assert.Single((await RequireBookingAsync(order.BookingId)).Invoice!.Payments).Status);

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));

        var booking = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
        Assert.Equal(BookingCancelReason.PAYMENT_EXPIRED, booking.CancelReason);
        Assert.Equal(PaymentTxStatus.SUCCESS, Assert.Single(booking.Invoice!.Payments).Status);
        Assert.Equal(InvoicePaymentStatus.EXPIRED, booking.Invoice.PaymentStatus);
        var refund = Assert.Single(await ReadRefundsAsync(order.BookingId));
        Assert.Equal(order.Amount, refund.Amount);
        Assert.Equal(100m, refund.RefundPercent);

        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));
        var refunded = await RequireBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.CANCELLED_REFUNDED, refunded.Status);
        Assert.Equal(InvoicePaymentStatus.REFUNDED, refunded.Invoice!.PaymentStatus);
    }

    [Fact]
    public async Task PaymentExpiry_OrderStillWithinItsWindow_IsUntouched()
    {
        var order = await BookPendingAsync();

        await RunPaymentExpiryJobAsync();

        var booking = await ReadApprovalBookingAsync(order.BookingId);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.NotEmpty(booking.SlotLocks);
        Assert.Equal(PaymentTxStatus.PENDING, Assert.Single(booking.Invoice!.Payments).Status);
    }

    // ───────────────────────── Slot lock và khoảng chết (C5) ─────────────────────────

    [Fact]
    public async Task PaymentExpiry_FreesTheSlotForTheNextCustomer()
    {
        var order = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId);

        await RunPaymentExpiryJobAsync();

        Assert.Empty(await ReadLocksAsync(order.BookingId));
        var (next, _) = await BookAsync(order.Court);
        await AssertStatusAsync(HttpStatusCode.Created, next);
    }

    [Fact]
    public async Task DeadTimeBeforeTheJobRuns_LatecomerIsTurnedAwayThenNotifiedWhenTheJobCancels()
    {
        var order = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId); // slot lock đã hết hạn nhưng đơn vẫn PENDING, booking_courts còn hiệu lực
        var latecomer = await SeedUserAsync();
        var (turnedAway, _) = await BookAsync(order.Court, customer: latecomer);
        await AssertStatusAsync(HttpStatusCode.BadRequest, turnedAway);
        Assert.Equal("SLOT_UNAVAILABLE_NOTIFY_REGISTERED", turnedAway.Code);
        var factory = await GetFactoryAsync();

        await RunPaymentExpiryJobAsync();

        Assert.Single(factory.Emails.To(latecomer.Email));
        var (rebook, _) = await BookAsync(order.Court, customer: latecomer);
        await AssertStatusAsync(HttpStatusCode.Created, rebook);
    }

    [Fact]
    public async Task StaleExpiredSlotLock_IsLeftForJob03_AndNeverBlocksANewBooking()
    {
        // C5: Job-01 không còn xóa slot lock hết hạn của toàn hệ thống (việc của Job-03, mỗi phút). Slot lock hết hạn còn sót không chặn ai:
        // mọi chỗ đọc lọc expires_at > now và đặt sân xóa slot lock hết hạn của chi nhánh trước khi chèn
        var order = await BookPendingAsync();
        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));
        var stale = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.SlotLocks.Add(new SlotLock
            {
                Id = stale, BookingId = order.BookingId, CourtId = order.Court.CourtId, Date = FutureDate(),
                StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
                ExpiresAt = DateTime.UtcNow.AddSeconds(-1), CreatedAt = DateTime.UtcNow.AddMinutes(-11)
            });
            return await db.SaveChangesAsync();
        });

        await RunPaymentExpiryJobAsync();
        Assert.True(await WithDbAsync(db => db.SlotLocks.AnyAsync(l => l.Id == stale)));

        var (next, _) = await BookAsync(order.Court);
        await AssertStatusAsync(HttpStatusCode.Created, next);
        Assert.False(await WithDbAsync(db => db.SlotLocks.AnyAsync(l => l.Id == stale)));
    }
}
