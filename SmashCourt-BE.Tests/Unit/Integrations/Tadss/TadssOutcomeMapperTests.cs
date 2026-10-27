using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Integrations.Tadss;

/// <summary>
/// BE-UT-09 (ánh xạ outcome, gói 6A-5 Task 6, spec 4.4.1, P6-26): booking ⇒ outcome gửi TADSS và <c>occurredAt</c>. Nhãn khách đến / không
/// đến phải khớp <c>checked_in_at</c> (C1), lệch thì <c>Invalid</c> (giữ lại, không đoán); <c>PENDING_PAYMENT</c> đã check-in là
/// <c>COMPLETED</c>; <c>occurredAt</c> của đơn đã chơi là giờ kết thúc muộn nhất (giờ VN, +07:00), chưa tới thì <c>NotDue</c>; của đơn hủy là
/// <c>cancelled_at</c> (giữ µs); <c>SYSTEM_CLEANUP</c> không gửi. Hàm thuần: gọi lại ra cùng nội dung (TADSS chống trùng theo nội dung).
/// </summary>
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-09")]
public class TadssOutcomeMapperTests
{
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);
    private static readonly DateOnly PlayDate = new(2026, 11, 20);
    // 17:00 giờ VN ngày chơi
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime CancelledAt = new DateTime(2026, 11, 19, 2, 15, 30, DateTimeKind.Utc).AddTicks(1_234_560);

    private static Booking Played(BookingStatus status, bool checkedIn, params (TimeOnly Start, TimeOnly End)[] slots)
    {
        var booking = TestDataFactory.CreateBooking(status: status, bookingDate: PlayDate);
        booking.TadssDecisionId = Guid.NewGuid();
        booking.CheckedInAt = checkedIn ? Now.AddHours(-3) : null;
        foreach (var (start, end) in slots.Length == 0 ? new[] { (new TimeOnly(15, 0), new TimeOnly(16, 0)) } : slots)
            booking.BookingCourts.Add(new BookingCourt { CourtId = Guid.NewGuid(), Date = PlayDate, StartTime = start, EndTime = end });
        return booking;
    }

    private static Booking Cancelled(BookingStatus status, BookingCancelReason? reason, DateTime? cancelledAt)
    {
        var booking = Played(status, checkedIn: false);
        booking.CancelReason = reason;
        booking.CancelledAt = cancelledAt;
        return booking;
    }

    private static TadssOutcomeRequestDto AssertSend(TadssOutcomePlan plan, TadssOutcome expected)
    {
        Assert.Equal(TadssOutcomePlanKind.Send, plan.Kind);
        Assert.Null(plan.Problem);
        Assert.NotNull(plan.Request);
        Assert.Equal(expected, plan.Request.Outcome);
        return plan.Request;
    }

    private static void AssertInvalid(TadssOutcomePlan plan, string mentions)
    {
        Assert.Equal(TadssOutcomePlanKind.Invalid, plan.Kind);
        Assert.Null(plan.Request);
        Assert.NotNull(plan.Problem);
        Assert.Contains(mentions, plan.Problem);
    }

    // ── Đã chơi: COMPLETED (gồm PENDING_PAYMENT, C1) ──────────────────────

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.PENDING_PAYMENT)] // nhân viên chưa check-out; khách đã đến
    public void Plan_CheckedInAndPlayEnded_SendsCompletedAtPlayEndInVietnamTime(BookingStatus status)
    {
        var request = AssertSend(TadssOutcomeMapper.Plan(Played(status, checkedIn: true), Now), TadssOutcome.COMPLETED);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 16, 0, 0, Vietnam), request.OccurredAt);
        Assert.Equal(Vietnam, request.OccurredAt.Offset);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.PENDING_PAYMENT)]
    public void Plan_CompletedWithoutCheckIn_IsInvalid(BookingStatus status)
    {
        AssertInvalid(TadssOutcomeMapper.Plan(Played(status, checkedIn: false), Now), "checked_in_at");
    }

    // ── Không đến ─────────────────────────────────────────────────────────

    [Fact]
    public void Plan_NoShowWithoutCheckIn_SendsNoShowAtPlayEnd()
    {
        var request = AssertSend(TadssOutcomeMapper.Plan(Played(BookingStatus.NO_SHOW, checkedIn: false), Now), TadssOutcome.NO_SHOW);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 16, 0, 0, Vietnam), request.OccurredAt);
    }

    [Fact]
    public void Plan_NoShowThatHasCheckIn_IsInvalid()
    {
        // Dữ liệu cũ của lỗi B6: Job-04 đè lên lần check-in
        AssertInvalid(TadssOutcomeMapper.Plan(Played(BookingStatus.NO_SHOW, checkedIn: true), Now), "checked_in_at");
    }

    // ── Giờ kết thúc ──────────────────────────────────────────────────────

    [Fact]
    public void Plan_CheckedOutBeforePlayEnd_IsNotDueUntilTheEndThenSends()
    {
        var booking = Played(BookingStatus.COMPLETED, checkedIn: true, (new TimeOnly(16, 0), new TimeOnly(18, 0)));

        Assert.Equal(TadssOutcomePlanKind.NotDue, TadssOutcomeMapper.Plan(booking, Now).Kind);
        Assert.Equal(TadssOutcomePlanKind.NotDue, TadssOutcomeMapper.Plan(booking, Now.AddHours(1).AddTicks(-1)).Kind);

        var request = AssertSend(TadssOutcomeMapper.Plan(booking, Now.AddHours(1)), TadssOutcome.COMPLETED);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 18, 0, 0, Vietnam), request.OccurredAt);
    }

    [Fact]
    public void Plan_LateEveningSlot_KeepsTheVietnamDate()
    {
        var booking = Played(BookingStatus.NO_SHOW, checkedIn: false, (new TimeOnly(23, 0), new TimeOnly(23, 59)));

        var request = AssertSend(TadssOutcomeMapper.Plan(booking, Now.AddDays(1)), TadssOutcome.NO_SHOW);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 23, 59, 0, Vietnam), request.OccurredAt);
        Assert.Equal(new DateTime(2026, 11, 20, 16, 59, 0, DateTimeKind.Utc), request.OccurredAt.UtcDateTime);
    }

    [Fact]
    public void Plan_TwoCourts_UsesTheLatestEnd()
    {
        var booking = Played(BookingStatus.COMPLETED, checkedIn: true,
            (new TimeOnly(14, 0), new TimeOnly(15, 0)), (new TimeOnly(15, 0), new TimeOnly(16, 30)));

        var request = AssertSend(TadssOutcomeMapper.Plan(booking, Now), TadssOutcome.COMPLETED);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 16, 30, 0, Vietnam), request.OccurredAt);
    }

    [Theory]
    [InlineData(BookingStatus.COMPLETED, true)]
    [InlineData(BookingStatus.NO_SHOW, false)]
    public void Plan_PlayedOrderWithoutCourts_IsInvalid(BookingStatus status, bool checkedIn)
    {
        var booking = Played(status, checkedIn);
        booking.BookingCourts.Clear();

        AssertInvalid(TadssOutcomeMapper.Plan(booking, Now), "sân");
    }

    // ── Hủy ───────────────────────────────────────────────────────────────

    public static TheoryData<BookingStatus, BookingCancelReason, TadssOutcome> CancelCases()
    {
        var data = new TheoryData<BookingStatus, BookingCancelReason, TadssOutcome>();
        foreach (var status in new[] { BookingStatus.CANCELLED, BookingStatus.CANCELLED_PENDING_REFUND, BookingStatus.CANCELLED_REFUNDED })
        {
            data.Add(status, BookingCancelReason.PAYMENT_EXPIRED, TadssOutcome.PAYMENT_EXPIRED);
            data.Add(status, BookingCancelReason.APPROVAL_EXPIRED, TadssOutcome.APPROVAL_EXPIRED);
            data.Add(status, BookingCancelReason.APPROVAL_REJECTED, TadssOutcome.REJECTED);
            data.Add(status, BookingCancelReason.POLICY_REJECTED, TadssOutcome.REJECTED);
            data.Add(status, BookingCancelReason.CUSTOMER_CANCELLED, TadssOutcome.CANCELLED_BY_CUSTOMER);
            data.Add(status, BookingCancelReason.STAFF_CANCELLED, TadssOutcome.CANCELLED_BY_STAFF);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CancelCases))]
    public void Plan_CancelledOrder_SendsByReasonAtCancelledAtKeepingMicroseconds(
        BookingStatus status, BookingCancelReason reason, TadssOutcome expected)
    {
        var request = AssertSend(TadssOutcomeMapper.Plan(Cancelled(status, reason, CancelledAt), Now), expected);

        Assert.Equal(new DateTimeOffset(CancelledAt), request.OccurredAt);
        Assert.Equal(TimeSpan.Zero, request.OccurredAt.Offset);
        Assert.Equal(CancelledAt.Ticks, request.OccurredAt.UtcTicks);
    }

    [Fact]
    public void Plan_CancelledAtReadWithoutKind_IsUnderstoodAsUtc()
    {
        var unspecified = DateTime.SpecifyKind(CancelledAt, DateTimeKind.Unspecified);

        var request = AssertSend(TadssOutcomeMapper.Plan(Cancelled(BookingStatus.CANCELLED, BookingCancelReason.PAYMENT_EXPIRED, unspecified), Now),
            TadssOutcome.PAYMENT_EXPIRED);

        Assert.Equal(CancelledAt.Ticks, request.OccurredAt.UtcTicks);
    }

    [Fact]
    public void Plan_SystemCleanup_IsSkipped()
    {
        var plan = TadssOutcomeMapper.Plan(Cancelled(BookingStatus.CANCELLED, BookingCancelReason.SYSTEM_CLEANUP, CancelledAt), Now);

        Assert.Equal(TadssOutcomePlanKind.Skip, plan.Kind);
        Assert.Null(plan.Request);
    }

    [Fact]
    public void Plan_CancelledWithoutReason_IsInvalid()
    {
        AssertInvalid(TadssOutcomeMapper.Plan(Cancelled(BookingStatus.CANCELLED, null, CancelledAt), Now), "cancel_reason");
    }

    [Fact]
    public void Plan_CancelledWithoutCancelledAt_IsInvalid()
    {
        AssertInvalid(TadssOutcomeMapper.Plan(Cancelled(BookingStatus.CANCELLED, BookingCancelReason.STAFF_CANCELLED, null), Now), "cancelled_at");
    }

    // ── Chưa kết thúc ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.IN_PROGRESS)]
    public void Plan_OrderNotFinished_IsNotDue(BookingStatus status)
    {
        var plan = TadssOutcomeMapper.Plan(Played(status, checkedIn: status == BookingStatus.IN_PROGRESS), Now.AddDays(1));

        Assert.Equal(TadssOutcomePlanKind.NotDue, plan.Kind);
        Assert.Null(plan.Request);
    }

    // ── Tất định ──────────────────────────────────────────────────────────

    [Fact]
    public void Plan_CalledTwiceLater_ProducesTheSameContent()
    {
        // Gửi lại ở chu kỳ sau (mất phản hồi lần trước) phải cùng nội dung để TADSS trả 200 bản đã lưu, không 409
        var played = Played(BookingStatus.PENDING_PAYMENT, checkedIn: true);
        var first = TadssOutcomeMapper.Plan(played, Now).Request;
        played.Status = BookingStatus.COMPLETED; // nhân viên check-out sau lần gửi đầu
        var second = TadssOutcomeMapper.Plan(played, Now.AddHours(5)).Request;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.OccurredAt, second.OccurredAt);
    }
}
