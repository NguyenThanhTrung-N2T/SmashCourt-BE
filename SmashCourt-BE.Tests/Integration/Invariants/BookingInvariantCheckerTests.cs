using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// BE-IT-20, từng điều của SC6 trên schema thật (plan 6A-6 Task 9): bộ dữ liệu hợp lệ ⇒ 0 vi phạm; làm hỏng đúng một điểm ⇒ đúng điều,
/// đúng đơn. Biên thời gian so với "now" của lượt kiểm tính cả dấu bằng.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-20")]
public sealed class BookingInvariantCheckerTests(PostgreSqlIntegrationFixture fixture) : InvariantTestBase(fixture)
{
    [Fact]
    public void CourtsReleasedWhileActive_IsASubsetOfActiveStatuses()
    {
        Assert.NotEmpty(BookingInvariantChecker.CourtsReleasedWhileActive);
        Assert.All(BookingInvariantChecker.CourtsReleasedWhileActive,
            s => Assert.Contains(s, BookingStatusTransition.GetActiveStatuses()));
    }

    [Fact]
    public async Task ValidOrdersInEveryStatus_HaveNoViolation()
    {
        var pending = await SeedOrderAsync();
        var awaiting = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL);
        var paid = await SeedDepositOrderAsync(BookingStatus.PAID_ONLINE, 200_000m, (PaymentTxStatus.SUCCESS, 200_000m));
        var zero = await SeedDepositOrderAsync(BookingStatus.CONFIRMED, 0m);
        var cancelled = await SeedCancelledOrderAsync(BookingStatus.CANCELLED_PENDING_REFUND);
        var playing = await SeedOrderAsync(BookingStatus.IN_PROGRESS);
        var completed = await SeedOrderAsync(BookingStatus.COMPLETED);

        AssertNone(await CheckAsync(pending.Booking.Id, awaiting.Booking.Id, paid.Booking.Id, zero.Booking.Id,
            cancelled.Booking.Id, playing.Booking.Id, completed.Booking.Id));
    }

    // ───────────────────────── SC6-1: đã xác nhận ⇒ đủ tiền cọc ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CONFIRMED)]
    public async Task Sc61_SuccessPaymentsCoverTheDeposit_NoViolation(BookingStatus status)
    {
        var exact = await SeedDepositOrderAsync(status, 200_000m, (PaymentTxStatus.SUCCESS, 200_000m));
        var more = await SeedDepositOrderAsync(status, 200_000m, (PaymentTxStatus.SUCCESS, 250_000m));
        var twoLinks = await SeedDepositOrderAsync(status, 200_000m, (PaymentTxStatus.SUCCESS, 100_000m), (PaymentTxStatus.SUCCESS, 100_000m));

        AssertNone(await CheckAsync(exact.Booking.Id, more.Booking.Id, twoLinks.Booking.Id));
    }

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CONFIRMED)]
    public async Task Sc61_SuccessPaymentsBelowTheDeposit_Violation(BookingStatus status)
    {
        var order = await SeedDepositOrderAsync(status, 200_000m, (PaymentTxStatus.SUCCESS, 100_000m), (PaymentTxStatus.SUCCESS, 99_999m));

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.ConfirmedIsPaid, order.Booking.Id);
    }

    [Theory]
    [InlineData(PaymentTxStatus.PENDING)]
    [InlineData(PaymentTxStatus.FAILED)]
    [InlineData(PaymentTxStatus.REFUNDED)]
    public async Task Sc61_OnlyNonSuccessPayments_DoNotCount(PaymentTxStatus paymentStatus)
    {
        var order = await SeedDepositOrderAsync(BookingStatus.PAID_ONLINE, 200_000m, (paymentStatus, 200_000m));

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.ConfirmedIsPaid, order.Booking.Id);
    }

    [Fact]
    public async Task Sc61_SuccessPaymentOfAnotherInvoice_DoesNotCount()
    {
        var order = await SeedDepositOrderAsync(BookingStatus.PAID_ONLINE, 200_000m);
        var other = await SeedDepositOrderAsync(BookingStatus.PAID_ONLINE, 200_000m, (PaymentTxStatus.SUCCESS, 400_000m));

        var violations = await CheckAsync(order.Booking.Id, other.Booking.Id);

        AssertOnly(violations, InvariantRules.ConfirmedIsPaid, order.Booking.Id);
    }

    [Fact]
    public async Task Sc61_ZeroDepositAndCounterBooking_AreNotRequiredToPay()
    {
        var zero = await SeedDepositOrderAsync(BookingStatus.CONFIRMED, 0m);
        var counter = await SeedOrderAsync(BookingStatus.CONFIRMED); // tại quầy: DepositSource null, không Payment

        AssertNone(await CheckAsync(zero.Booking.Id, counter.Booking.Id));
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.IN_PROGRESS)]
    [InlineData(BookingStatus.COMPLETED)]
    public async Task Sc61_OtherStatuses_AreNotChecked(BookingStatus status)
    {
        var order = await SeedDepositOrderAsync(status, 200_000m);

        AssertNone(await CheckAsync(order.Booking.Id));
    }

    // ───────────────────────── SC6-2: đang chờ duyệt ⇒ giữ slot lock còn hạn ─────────────────────────

    [Fact]
    public async Task Sc62_LocksMatchEveryCourt_NoViolation()
    {
        var order = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL, courtCount: 2);

        AssertNone(await CheckAsync(order.Booking.Id));
    }

    [Fact]
    public async Task Sc62_OneOfTwoCourtsWithoutLock_OneViolation()
    {
        var order = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL, courtCount: 2);
        DbContext.SlotLocks.Remove(order.Locks[1]);
        await DbContext.SaveChangesAsync();

        var violations = await CheckAsync(order.Booking.Id);

        AssertOnly(violations, InvariantRules.AwaitingApprovalHoldsLocks, order.Booking.Id);
        Assert.Contains(order.Courts[1].Id.ToString(), violations[0].Detail);
    }

    [Fact]
    public async Task Sc62_LockExpiringExactlyNow_Violation()
    {
        var order = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL);
        order.Locks[0].ExpiresAt = Now;
        await DbContext.SaveChangesAsync();

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.AwaitingApprovalHoldsLocks, order.Booking.Id);
    }

    [Fact]
    public async Task Sc62_LockForADifferentTime_Violation()
    {
        var order = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL);
        order.Locks[0].EndTime = new TimeOnly(18, 30);
        await DbContext.SaveChangesAsync();

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.AwaitingApprovalHoldsLocks, order.Booking.Id);
    }

    // Đơn đã quá hạn duyệt nhưng job chưa chạy (≤ 1 chu kỳ) không bị báo nhầm
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public async Task Sc62_ApprovalDeadlineBoundary_WithoutLock(int secondsFromNow, int expectedViolations)
    {
        var order = await SeedOrderAsync(BookingStatus.PENDING_APPROVAL);
        order.Booking.ApprovalExpiresAt = Now.AddSeconds(secondsFromNow);
        DbContext.SlotLocks.RemoveRange(order.Locks);
        await DbContext.SaveChangesAsync();

        Assert.Equal(expectedViolations, (await CheckAsync(order.Booking.Id)).Count(v => v.Rule == InvariantRules.AwaitingApprovalHoldsLocks));
    }

    // ───────────────────────── SC6-3 và SC6-6: đã hủy ⇒ không giữ gì, có lý do ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    public async Task Sc63_CancelledOrderReleasedEverything_NoViolation(BookingStatus status)
    {
        var order = await SeedCancelledOrderAsync(status);

        AssertNone(await CheckAsync(order.Booking.Id));
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    public async Task Sc63_CancelledOrderWithAFutureLock_Violation(BookingStatus status)
    {
        var order = await SeedCancelledOrderAsync(status);
        order.Locks[0].ExpiresAt = Now.AddSeconds(1);
        await DbContext.SaveChangesAsync();

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.CancelledHoldsNothing, order.Booking.Id);
    }

    [Fact]
    public async Task Sc63_CancelledOrderWithAnActiveCourtLine_Violation()
    {
        var order = await SeedCancelledOrderAsync();
        (await DbContext.BookingCourts.SingleAsync(c => c.BookingId == order.Booking.Id)).IsActive = true;
        await DbContext.SaveChangesAsync();

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.CancelledHoldsNothing, order.Booking.Id);
    }

    // DB nhận đơn hủy thiếu lý do (chk_bookings_cancel_reason chỉ cấm lý do ở đơn chưa hủy); mức ảnh hưởng theo nơi đọc lý do
    [Theory]
    [InlineData(true, true, CancelSourceEnum.CUSTOMER, "CRITICAL: outcome về TADSS không gửi được", "lịch sử khách")]
    [InlineData(true, false, CancelSourceEnum.SYSTEM, "CRITICAL: outcome về TADSS không gửi được", null)]
    [InlineData(false, true, CancelSourceEnum.LINK, "HIGH: lịch sử khách gửi TADSS bỏ sót", null)]
    [InlineData(false, false, CancelSourceEnum.STAFF, "LOW: khách vãng lai", null)]
    public async Task Sc66_CancelledWithoutReason_ViolationStatesTheImpact(
        bool hasDecision, bool hasCustomer, CancelSourceEnum source, string impact, string? alsoMentions)
    {
        var order = await SeedCancelledOrderAsync();
        order.Booking.CancelReason = null;
        order.Booking.CancelSource = source;
        order.Booking.TadssDecisionId = hasDecision ? Guid.NewGuid() : null;
        if (!hasCustomer)
        {
            order.Booking.CustomerId = null;
            order.Booking.GuestName = "Khách vãng lai"; // chk_bookings_guest_info
        }
        await DbContext.SaveChangesAsync();

        var violations = await CheckAsync(order.Booking.Id);

        AssertOnly(violations, InvariantRules.CancelledHasReason, order.Booking.Id);
        Assert.Contains(impact, violations[0].Detail);
        Assert.Contains($"cancel_source {source}", violations[0].Detail);
        if (alsoMentions != null)
            Assert.Contains(alsoMentions, violations[0].Detail);
    }

    // cancel_source đọc từ chính đơn: đơn không có hóa đơn vẫn ghi đúng; đơn cũ thiếu cả nguồn hủy ghi "trống"
    [Fact]
    public async Task Sc66_WithoutInvoice_StillReportsTheCancelSourceOfTheBooking()
    {
        var withoutInvoice = await SeedCancelledOrderAsync();
        withoutInvoice.Booking.CancelReason = null;
        withoutInvoice.Booking.CancelSource = CancelSourceEnum.STAFF;
        DbContext.Invoices.Remove(withoutInvoice.Invoice);
        var noSource = await SeedCancelledOrderAsync();
        noSource.Booking.CancelReason = null;
        noSource.Booking.CancelSource = null;
        await DbContext.SaveChangesAsync();

        var violations = await CheckAsync(withoutInvoice.Booking.Id, noSource.Booking.Id);

        Assert.Equal(2, violations.Count);
        Assert.All(violations, v => Assert.Equal(InvariantRules.CancelledHasReason, v.Rule));
        Assert.Contains("cancel_source STAFF", Assert.Single(violations, v => v.BookingId == withoutInvoice.Booking.Id).Detail);
        Assert.Contains("cancel_source trống", Assert.Single(violations, v => v.BookingId == noSource.Booking.Id).Detail);
    }

    [Theory]
    [InlineData(BookingCancelReason.PAYMENT_EXPIRED)]
    [InlineData(BookingCancelReason.APPROVAL_EXPIRED)]
    [InlineData(BookingCancelReason.APPROVAL_REJECTED)]
    [InlineData(BookingCancelReason.POLICY_REJECTED)]
    [InlineData(BookingCancelReason.CUSTOMER_CANCELLED)]
    [InlineData(BookingCancelReason.STAFF_CANCELLED)]
    [InlineData(BookingCancelReason.SYSTEM_CLEANUP)]
    public async Task Sc66_AnyReason_NoViolation(BookingCancelReason reason)
    {
        var order = await SeedCancelledOrderAsync();
        order.Booking.CancelReason = reason;
        order.Booking.TadssDecisionId = Guid.NewGuid();
        await DbContext.SaveChangesAsync();

        AssertNone(await CheckAsync(order.Booking.Id));
    }

    // ───────────────────────── Phụ-2: PENDING ⇒ có hạn giữ chỗ ─────────────────────────

    [Fact]
    public async Task Phu2_PendingWithoutHoldDeadline_Violation()
    {
        var order = await SeedOrderAsync();
        order.Invoice.HoldDeadline = null;
        await DbContext.SaveChangesAsync();

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.PendingHasHoldDeadline, order.Booking.Id);
    }

    [Fact]
    public async Task Phu2_PendingWithoutInvoice_Violation()
    {
        var order = await SeedOrderAsync();
        DbContext.Invoices.Remove(order.Invoice);
        await DbContext.SaveChangesAsync();

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.PendingHasHoldDeadline, order.Booking.Id);
    }
}
