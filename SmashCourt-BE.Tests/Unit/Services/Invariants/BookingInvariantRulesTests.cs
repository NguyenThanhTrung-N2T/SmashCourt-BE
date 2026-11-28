using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Services.Invariants;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services.Invariants;

/// <summary>
/// Luật thuần của bộ kiểm "0 booking hỏng" trên ảnh chụp dựng tay (không DB): mỗi điều có ca vi phạm và ca biên không vi phạm. Cùng bộ luật chạy trên
/// PostgreSQL thật ở <c>Integration/Invariants</c> (BE-IT-20) và ở job kiểm định kỳ của bản deploy.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "OB-BE-5")]
public sealed class BookingInvariantRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Court = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly DateOnly Day = new(2026, 11, 21);

    private static InvariantBookingRow Booking(BookingStatus status, Guid? id = null, DateTime? approvalExpiresAt = null,
        BookingCancelReason? reason = BookingCancelReason.CUSTOMER_CANCELLED, bool decision = false, bool customer = true)
    {
        var bookingId = id ?? Guid.NewGuid();
        return new(bookingId, status, approvalExpiresAt, reason, CancelSourceEnum.CUSTOMER, decision ? Guid.NewGuid() : null,
            customer ? Guid.NewGuid() : null, CodeOf(bookingId));
    }

    private static string CodeOf(Guid bookingId) => $"BK-TEST-{bookingId.ToString("N")[..8]}";

    private static InvariantInvoiceRow Invoice(Guid bookingId, DepositSource? source = null, decimal? deposit = null, decimal paid = 0m,
        InvoicePaymentStatus status = InvoicePaymentStatus.UNPAID, decimal finalTotal = 200_000m, decimal? collected = null,
        DateTime? holdDeadline = null) =>
        new(bookingId, source, deposit, holdDeadline ?? Now.AddMinutes(10), status, finalTotal, paid, collected ?? paid);

    private static InvariantCourtLine Line(Guid bookingId, int startHour = 18, int endHour = 20, bool active = true, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), bookingId, Court, Day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), active);

    private static InvariantLockRow Lock(Guid bookingId, DateTime expiresAt, int startHour = 18, int endHour = 20) =>
        new(bookingId, Court, Day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), expiresAt);

    private static IReadOnlyList<InvariantViolation> Evaluate(
        InvariantBookingRow booking, InvariantInvoiceRow? invoice = null, IReadOnlyList<InvariantCourtLine>? lines = null,
        IReadOnlyList<InvariantLockRow>? locks = null, IReadOnlyList<InvariantCourtLine>? holding = null)
    {
        var invoices = invoice == null ? new Dictionary<Guid, InvariantInvoiceRow>() : new Dictionary<Guid, InvariantInvoiceRow> { [booking.Id] = invoice };
        return BookingInvariantRules.Evaluate(
            new BookingInvariantSnapshot([booking.Id], [booking], invoices, lines ?? [], locks ?? [], holding ?? []), Now);
    }

    private static void AssertOnly(IReadOnlyList<InvariantViolation> violations, string rule) =>
        Assert.True(violations.Count > 0 && violations.All(v => v.Rule == rule), $"Kỳ vọng chỉ {rule}, thực tế: {string.Join("; ", violations)}");

    [Fact]
    public void Evaluate_WhenScopeIsEmpty_ReturnsNothing() =>
        Assert.Empty(BookingInvariantRules.Evaluate(BookingInvariantSnapshot.Empty, Now));

    [Fact]
    public void Evaluate_WhenNowIsNotUtc_Throws() =>
        Assert.Throws<ArgumentException>(() => BookingInvariantRules.Evaluate(BookingInvariantSnapshot.Empty, DateTime.SpecifyKind(Now, DateTimeKind.Local)));

    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE, 50_000, true)]
    [InlineData(BookingStatus.CONFIRMED, 99_999, true)]
    [InlineData(BookingStatus.PAID_ONLINE, 100_000, false)]
    [InlineData(BookingStatus.PENDING, 0, false)]
    public void Evaluate_ConfirmedDepositOrder_MustHaveTheDepositPaid(BookingStatus status, int paid, bool violated)
    {
        var booking = Booking(status);

        var violations = Evaluate(booking, Invoice(booking.Id, DepositSource.TADSS, 100_000m, paid));

        Assert.Equal(violated, violations.Any(v => v.Rule == InvariantRules.ConfirmedIsPaid));
    }

    [Fact]
    public void Evaluate_ConfirmedOrderWithoutDepositFlow_IsNotCheckedForDeposit()
    {
        var booking = Booking(BookingStatus.CONFIRMED);

        Assert.DoesNotContain(Evaluate(booking, Invoice(booking.Id)), v => v.Rule == InvariantRules.ConfirmedIsPaid);
    }

    [Theory]
    [InlineData(199_999, true)]
    [InlineData(200_000, false)]
    [InlineData(250_000, false)] // thu thừa được hoàn tại chi nhánh, không phải vi phạm
    public void Evaluate_PaidInvoice_MustBeCollectedInFull(int collected, bool violated)
    {
        var booking = Booking(BookingStatus.COMPLETED);

        var violations = Evaluate(booking, Invoice(booking.Id, status: InvoicePaymentStatus.PAID, paid: collected, collected: collected));

        Assert.Equal(violated, violations.Any(v => v.Rule == InvariantRules.PaidInvoiceIsCollected));
    }

    [Fact]
    public void Evaluate_AwaitingApprovalWithoutLiveLock_IsReported()
    {
        var booking = Booking(BookingStatus.PENDING_APPROVAL, approvalExpiresAt: Now.AddMinutes(20));

        var withExpiredLock = Evaluate(booking, Invoice(booking.Id), [Line(booking.Id)], [Lock(booking.Id, Now.AddSeconds(-1))]);
        var withLiveLock = Evaluate(booking, Invoice(booking.Id), [Line(booking.Id)], [Lock(booking.Id, Now.AddMinutes(20))]);

        AssertOnly(withExpiredLock, InvariantRules.AwaitingApprovalHoldsLocks);
        Assert.Empty(withLiveLock);
    }

    [Fact]
    public void Evaluate_AwaitingApprovalPastItsDeadline_IsLeftToTheExpiryJob()
    {
        var booking = Booking(BookingStatus.PENDING_APPROVAL, approvalExpiresAt: Now.AddMinutes(-1));

        Assert.Empty(Evaluate(booking, Invoice(booking.Id), [Line(booking.Id)]));
    }

    [Fact]
    public void Evaluate_CancelledOrderStillHolding_IsReported()
    {
        var booking = Booking(BookingStatus.CANCELLED);

        var violations = Evaluate(booking, Invoice(booking.Id), [Line(booking.Id, active: true)], [Lock(booking.Id, Now.AddMinutes(5))]);
        var released = Evaluate(booking, Invoice(booking.Id), [Line(booking.Id, active: false)], [Lock(booking.Id, Now.AddMinutes(-5))]);

        Assert.Equal(2, violations.Count(v => v.Rule == InvariantRules.CancelledHoldsNothing));
        Assert.Empty(released);
    }

    [Theory]
    [InlineData(true, true, "CRITICAL")]
    [InlineData(true, false, "CRITICAL")]
    [InlineData(false, true, "HIGH")]
    [InlineData(false, false, "LOW")]
    public void Evaluate_CancelledWithoutReason_ReportsTheImpact(bool decision, bool customer, string impact)
    {
        var booking = Booking(BookingStatus.CANCELLED, reason: null, decision: decision, customer: customer);

        var violation = Assert.Single(Evaluate(booking, Invoice(booking.Id)));

        Assert.Equal(InvariantRules.CancelledHasReason, violation.Rule);
        Assert.Contains(impact, violation.Detail);
    }

    [Theory]
    [InlineData(BookingStatus.CONFIRMED, true)]
    [InlineData(BookingStatus.IN_PROGRESS, true)]
    [InlineData(BookingStatus.PENDING_PAYMENT, false)] // job kết thúc giờ chơi được phép nhả sân khi chưa thu đủ
    [InlineData(BookingStatus.COMPLETED, false)]
    public void Evaluate_ActiveOrderWithReleasedCourt_IsReportedExceptWhereReleaseIsAllowed(BookingStatus status, bool violated)
    {
        var booking = Booking(status);

        var violations = Evaluate(booking, Invoice(booking.Id), [Line(booking.Id, active: false)]);

        Assert.Equal(violated, violations.Any(v => v.Rule == InvariantRules.ActiveCourtsAreActive));
    }

    [Fact]
    public void Evaluate_PendingOrder_MustHaveAHoldDeadline()
    {
        var booking = Booking(BookingStatus.PENDING);
        var noDeadline = Invoice(booking.Id) with { HoldDeadline = null };

        AssertOnly(Evaluate(booking), InvariantRules.PendingHasHoldDeadline);
        AssertOnly(Evaluate(booking, noDeadline), InvariantRules.PendingHasHoldDeadline);
        Assert.Empty(Evaluate(booking, Invoice(booking.Id)));
    }

    // Mã đơn đi cùng vi phạm để log của job và dashboard chỉ ra đơn nhân viên tra được, không chỉ id
    [Fact]
    public void Evaluate_Violation_CarriesTheBookingCode()
    {
        var booking = Booking(BookingStatus.PENDING);

        var violation = Assert.Single(Evaluate(booking));

        Assert.Equal(CodeOf(booking.Id), violation.BookingCode);
        Assert.Equal(CodeOf(booking.Id), violation.BookingLabel);
        Assert.Contains(CodeOf(booking.Id), violation.ToString());
    }

    [Fact]
    public void Evaluate_TwoOrdersHoldingOverlappingTime_IsReportedOnceForTheScopedOrder()
    {
        var scoped = Booking(BookingStatus.CONFIRMED);
        var other = Guid.NewGuid();
        var mine = Line(scoped.Id, 18, 20);

        var violations = Evaluate(scoped, Invoice(scoped.Id), [mine], holding: [mine, Line(other, 19, 21)]);

        var violation = Assert.Single(violations);
        Assert.Equal(InvariantRules.NoDoubleBooking, violation.Rule);
        Assert.Equal(scoped.Id, violation.BookingId);
        Assert.Equal(CodeOf(scoped.Id), violation.BookingCode);
    }

    [Fact]
    public void Evaluate_BackToBackOrders_AreNotADoubleBooking()
    {
        var scoped = Booking(BookingStatus.CONFIRMED);
        var mine = Line(scoped.Id, 18, 20);

        Assert.Empty(Evaluate(scoped, Invoice(scoped.Id), [mine], holding: [mine, Line(Guid.NewGuid(), 20, 22)]));
    }

    [Fact]
    public void Evaluate_DoubleBookingOutsideTheScope_IsNotReported()
    {
        var scoped = Booking(BookingStatus.CONFIRMED);

        Assert.Empty(Evaluate(scoped, Invoice(scoped.Id), holding: [Line(Guid.NewGuid(), 18, 20), Line(Guid.NewGuid(), 19, 21)]));
    }

    [Fact]
    public void Evaluate_BothOrdersInScope_ReportsOnceOnTheSmallerId()
    {
        var a = Booking(BookingStatus.CONFIRMED, Guid.Parse("00000000-0000-0000-0000-00000000000a"));
        var b = Booking(BookingStatus.CONFIRMED, Guid.Parse("00000000-0000-0000-0000-00000000000b"));
        var lineA = Line(a.Id, 18, 20);
        var lineB = Line(b.Id, 19, 21);
        var snapshot = new BookingInvariantSnapshot([a.Id, b.Id], [a, b],
            new Dictionary<Guid, InvariantInvoiceRow> { [a.Id] = Invoice(a.Id), [b.Id] = Invoice(b.Id) }, [lineA, lineB], [], [lineA, lineB]);

        var violation = Assert.Single(BookingInvariantRules.Evaluate(snapshot, Now));

        Assert.Equal(a.Id, violation.BookingId);
        Assert.Equal(CodeOf(a.Id), violation.BookingCode);
    }

    [Fact]
    public void Evaluate_Violations_AreOrderedByRuleThenBooking()
    {
        var booking = Booking(BookingStatus.CANCELLED, reason: null);

        var violations = Evaluate(booking, Invoice(booking.Id), [Line(booking.Id, active: true)]);

        Assert.Equal(violations.OrderBy(v => v.Rule, StringComparer.Ordinal).Select(v => v.Rule), violations.Select(v => v.Rule));
        Assert.Equal([InvariantRules.CancelledHoldsNothing, InvariantRules.CancelledHasReason], violations.Select(v => v.Rule));
    }

    [Fact]
    public void DatabaseRules_ListEveryRuleExceptTheStagingOnlyOne()
    {
        var all = typeof(InvariantRules).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        Assert.Equal(all.Where(r => r != InvariantRules.DecisionExistsInTadss).Order(StringComparer.Ordinal), InvariantRules.DatabaseRules);
    }
}
