using System.Net;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.Integration.Invariants;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// BE-IT-20, bộ tự kiểm SC6 sau mỗi test HTTP (plan 6A-6 Task 10): phạm vi = đơn mới ∪ đơn có sẵn bị đổi <c>updated_at</c>. Mỗi đường ghi
/// chính (khách hủy, nhân viên hủy, IPN, duyệt, Job-01, check-out, xác nhận hoàn) phải đưa đơn có sẵn vào phạm vi; đơn có sẵn không bị đụng
/// thì không (R11). "Đơn có sẵn" giả lập bằng <see cref="HttpTestBase.RestartInvariantScopeAsync"/> sau khi dựng đơn.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Catalog", "BE-IT-20")]
public sealed class InvariantSelfCheckTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private async Task AssertInScopeAsync(Guid touched, Guid untouched)
    {
        var scope = await TouchedBookingIdsAsync();
        Assert.Contains(touched, scope);
        Assert.DoesNotContain(untouched, scope);
    }

    private Task ExpirePaymentWindowAsync(Guid bookingId) =>
        WithDbAsync(async db =>
        {
            var expired = DateTime.UtcNow.AddSeconds(-1);
            await db.SlotLocks.Where(l => l.BookingId == bookingId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, expired));
            // Như ExpiryConcurrencyTests: hạn thanh toán đã qua, hạn giữ chỗ còn (chk_invoices_expires_at)
            return await db.Invoices.Where(i => i.BookingId == bookingId).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.CreatedAt, DateTime.UtcNow.AddMinutes(-60))
                .SetProperty(i => i.HoldDeadline, (DateTime?)DateTime.UtcNow.AddMinutes(30))
                .SetProperty(i => i.ExpiresAt, (DateTime?)expired));
        });

    // ───────────────────────── Bộ tự kiểm bắt được vi phạm ─────────────────────────

    [Fact]
    public async Task ViolationLeftByTheTest_IsReported()
    {
        var order = await BookPendingAsync();
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == order.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.HoldDeadline, (DateTime?)null)));

        var violations = await CheckTouchedBookingsAsync();

        var violation = Assert.Single(violations);
        Assert.Equal(InvariantRules.PendingHasHoldDeadline, violation.Rule);
        Assert.Equal(order.BookingId, violation.BookingId);
        // Chính bước tự kiểm cuối test (DisposeAsync của nền) ném lỗi có tiền tố [SC6]
        var thrown = await Assert.ThrowsAsync<InvariantViolationException>(DisposeAsync);
        Assert.StartsWith("[SC6] 1 vi phạm", thrown.Message);
        Assert.Equal(violation, Assert.Single(thrown.Violations));

        // Trả dữ liệu đúng để lần tự kiểm cuối test đạt
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == order.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.HoldDeadline, (DateTime?)DateTime.UtcNow.AddMinutes(15))));
        Assert.Empty(await CheckTouchedBookingsAsync());
    }

    // R11 (a): đơn có sẵn bị test hủy mà (giả lập lỗi) còn slot lock ⇒ bị bắt nhờ updated_at
    [Fact]
    public async Task ExistingOrderCancelledInTheTest_LeftoverLockIsCaught()
    {
        var order = await BookPendingAsync();
        await RestartInvariantScopeAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));
        var court = await WithDbAsync(db => db.BookingCourts.AsNoTracking().FirstAsync(c => c.BookingId == order.BookingId));
        var leftover = TestDataSeeder.NewSlotLock(order.BookingId, court.CourtId, court.Date, court.StartTime, court.EndTime,
            DateTime.UtcNow.AddMinutes(10));
        await WithDbAsync(db => { db.SlotLocks.Add(leftover); return db.SaveChangesAsync(); });

        var violation = Assert.Single(await CheckTouchedBookingsAsync());
        Assert.Equal(InvariantRules.CancelledHoldsNothing, violation.Rule);
        Assert.Equal(order.BookingId, violation.BookingId);

        await WithDbAsync(db => db.SlotLocks.Where(l => l.Id == leftover.Id).ExecuteDeleteAsync());
    }

    // R11 (b): đơn có sẵn không bị đụng không nằm trong phạm vi, kể cả khi nó có vi phạm
    [Fact]
    public async Task UntouchedExistingOrder_IsOutOfScope()
    {
        var broken = await BookPendingAsync();
        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == broken.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.HoldDeadline, (DateTime?)null)));
        await RestartInvariantScopeAsync();

        var other = await BookPendingAsync();

        Assert.DoesNotContain(broken.BookingId, await TouchedBookingIdsAsync());
        Assert.Empty(await CheckTouchedBookingsAsync());
        Assert.Contains(other.BookingId, await TouchedBookingIdsAsync());

        await WithDbAsync(db => db.Invoices.Where(i => i.BookingId == broken.BookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.HoldDeadline, (DateTime?)DateTime.UtcNow.AddMinutes(15))));
    }

    // ───────────────────────── Đường ghi chính đưa đơn có sẵn vào phạm vi (R30) ─────────────────────────

    [Fact]
    public async Task CustomerCancel_BringsTheOrderIntoScope()
    {
        var order = await BookPendingAsync();
        var untouched = await BookPendingAsync();
        await RestartInvariantScopeAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await CustomerCancelAsync(order.Customer, order.BookingId));

        await AssertInScopeAsync(order.BookingId, untouched.BookingId);
    }

    [Fact]
    public async Task StaffCancel_BringsTheOrderIntoScope()
    {
        var order = await BookPaidAsync();
        var untouched = await BookPendingAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);
        await RestartInvariantScopeAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));

        await AssertInScopeAsync(order.BookingId, untouched.BookingId);
    }

    [Fact]
    public async Task SuccessfulIpn_BringsTheOrderIntoScope()
    {
        var order = await BookPendingAsync();
        var untouched = await BookPendingAsync();
        await RestartInvariantScopeAsync();

        Assert.Equal(HttpStatusCode.OK, await IpnAsync(order.TransactionRef, order.Amount));

        await AssertInScopeAsync(order.BookingId, untouched.BookingId);
    }

    [Fact]
    public async Task ManagerApproval_BringsTheOrderIntoScope()
    {
        var untouched = await BookPendingAsync(); // trước khi bật TADSS giả (đang trả NEED_APPROVAL)
        var tadss = await GetTadssAsync();
        using (await UseTadssAsync())
        {
            var pending = await CreatePendingApprovalAsync(tadss);
            var manager = await SeedManagerAsync(pending.Court.BranchId);
            tadss.ReplyOverride(pending.DecisionId, "APPROVE", manager.Id.ToString(), Reason, 0.3m, 60_000m);
            await RestartInvariantScopeAsync();

            await AssertApiStatusAsync(HttpStatusCode.OK, await ApproveAsync(await CreateClientForAsync(manager), pending.BookingId, 0.3m));

            await AssertInScopeAsync(pending.BookingId, untouched.BookingId);
        }
    }

    [Fact]
    public async Task PaymentExpiryJob_BringsTheOrderIntoScope()
    {
        var order = await BookPendingAsync();
        var untouched = await BookPendingAsync();
        await ExpirePaymentWindowAsync(order.BookingId);
        await RestartInvariantScopeAsync();

        await RunPaymentExpiryJobAsync();

        Assert.Equal(BookingStatus.CANCELLED, (await RequireBookingAsync(order.BookingId)).Status);
        await AssertInScopeAsync(order.BookingId, untouched.BookingId);
    }

    [Fact]
    public async Task Checkout_BringsTheOrderIntoScope()
    {
        var order = await BookPaidAsync();
        var untouched = await BookPendingAsync();
        await StartPlayingAsync(order.BookingId);
        var staff = await SeedStaffAsync(order.Court.BranchId);
        await RestartInvariantScopeAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await CheckoutAsync(staff, order.BookingId));

        await AssertInScopeAsync(order.BookingId, untouched.BookingId);
    }

    [Fact]
    public async Task RefundConfirmation_BringsTheOrderIntoScope()
    {
        await EnsureCancelPoliciesAsync();
        var order = await BookPaidAsync();
        var untouched = await BookPendingAsync();
        var staff = await SeedStaffAsync(order.Court.BranchId);
        await AssertApiStatusAsync(HttpStatusCode.OK, await StaffCancelAsync(staff, order.BookingId));
        await RestartInvariantScopeAsync();

        await AssertApiStatusAsync(HttpStatusCode.OK, await ConfirmRefundAsync(staff, order.BookingId));

        await AssertInScopeAsync(order.BookingId, untouched.BookingId);
    }
}
