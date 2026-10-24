using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Common;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Unit.Services.Deposit;

/// <summary>
/// BE-UT-05: chọn mã 409 theo DB của BE (DEPOSIT_APPROVAL_API 4.2 bước 4), xét đúng thứ tự:
/// (a) đã xử lý ⇒ APPROVAL_ALREADY_RESOLVED; (b) hết hạn duyệt ⇒ APPROVAL_EXPIRED; (c) không chờ duyệt ⇒ BOOKING_NOT_PENDING_APPROVAL;
/// rồi còn chờ mà thiếu mã quyết định ⇒ APPROVAL_DECISION_NOT_FOUND (Q10). Null = được gọi TADSS.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Helper)]
[Trait("Catalog", "BE-UT-05")]
public class ApprovalConflictResolverTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 2, 30, 0, DateTimeKind.Utc);

    private static Booking Pending(Action<Booking>? change = null)
    {
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PENDING_APPROVAL);
        booking.Source = BookingSource.ONLINE;
        booking.TadssDecisionId = Guid.NewGuid();
        booking.ApprovalExpiresAt = Now.AddMinutes(10);
        change?.Invoke(booking);
        return booking;
    }

    private static string? Code(Booking booking) => ApprovalConflictResolver.Resolve(booking, Now)?.ErrorCode;

    [Fact]
    public void StillPendingAndInTime_IsAllowed()
    {
        Assert.Null(ApprovalConflictResolver.Resolve(Pending(), Now));
    }

    [Fact]
    public void ExpiresOneTickLater_IsAllowed()
    {
        Assert.Null(Code(Pending(b => b.ApprovalExpiresAt = Now.AddTicks(1))));
    }

    // ── (a) đã xử lý ───────────────────────────────────────────────────────

    [Fact]
    public void Approved_IsAlreadyResolved()
    {
        var booking = Pending(b =>
        {
            b.Status = BookingStatus.PENDING;
            b.ApprovalResult = ApprovalResult.APPROVED;
            b.ApprovalResolvedAt = Now.AddMinutes(-1);
        });

        Assert.Equal(ErrorCodes.ApprovalAlreadyResolved, Code(booking));
    }

    [Fact]
    public void Rejected_IsAlreadyResolved()
    {
        var booking = Pending(b =>
        {
            b.Status = BookingStatus.CANCELLED;
            b.CancelReason = BookingCancelReason.APPROVAL_REJECTED;
            b.ApprovalResult = ApprovalResult.REJECTED;
            b.ApprovalResolvedAt = Now.AddMinutes(-1);
        });

        Assert.Equal(ErrorCodes.ApprovalAlreadyResolved, Code(booking));
    }

    [Fact]
    public void ApprovedThenCancelledForPaymentExpiry_IsAlreadyResolvedNotExpired()
    {
        // Rule a đứng trước: đã duyệt rồi bị hủy vì hết hạn thanh toán, hạn duyệt cũng đã qua
        var booking = Pending(b =>
        {
            b.Status = BookingStatus.CANCELLED;
            b.CancelReason = BookingCancelReason.PAYMENT_EXPIRED;
            b.ApprovalResult = ApprovalResult.APPROVED;
            b.ApprovalResolvedAt = Now.AddMinutes(-40);
            b.ApprovalExpiresAt = Now.AddMinutes(-20);
        });

        Assert.Equal(ErrorCodes.ApprovalAlreadyResolved, Code(booking));
    }

    // ── (b) hết hạn duyệt ──────────────────────────────────────────────────

    [Fact]
    public void PendingAndExpiresNow_IsExpired()
    {
        Assert.Equal(ErrorCodes.ApprovalExpired, Code(Pending(b => b.ApprovalExpiresAt = Now)));
    }

    [Fact]
    public void PendingAndAlreadyExpired_IsExpired()
    {
        Assert.Equal(ErrorCodes.ApprovalExpired, Code(Pending(b => b.ApprovalExpiresAt = Now.AddMinutes(-5))));
    }

    [Fact]
    public void CancelledByApprovalExpiryJob_IsExpired()
    {
        var booking = Pending(b =>
        {
            b.Status = BookingStatus.CANCELLED;
            b.CancelReason = BookingCancelReason.APPROVAL_EXPIRED;
            b.ApprovalExpiresAt = Now.AddMinutes(-1);
        });

        Assert.Equal(ErrorCodes.ApprovalExpired, Code(booking));
    }

    [Fact]
    public void ExpiredAndMissingDecision_IsExpiredBeforeDecisionNotFound()
    {
        var booking = Pending(b =>
        {
            b.ApprovalExpiresAt = Now.AddMinutes(-1);
            b.TadssDecisionId = null;
        });

        Assert.Equal(ErrorCodes.ApprovalExpired, Code(booking));
    }

    // ── (c) không chờ duyệt ────────────────────────────────────────────────

    [Fact]
    public void WalkIn_IsNotPendingApproval()
    {
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.CONFIRMED);
        booking.Source = BookingSource.WALK_IN;

        Assert.Equal(ErrorCodes.BookingNotPendingApproval, Code(booking));
    }

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.COMPLETED)]
    public void OnlineBookingThatNeverNeededApproval_IsNotPendingApproval(BookingStatus status)
    {
        var booking = TestDataFactory.CreateBooking(status: status);
        booking.Source = BookingSource.ONLINE;

        Assert.Equal(ErrorCodes.BookingNotPendingApproval, Code(booking));
    }

    [Theory]
    [InlineData(BookingCancelReason.CUSTOMER_CANCELLED)]
    [InlineData(BookingCancelReason.STAFF_CANCELLED)]
    public void CancelledWhileWaiting_IsNotPendingApproval(BookingCancelReason reason)
    {
        // Q8: khách/nhân viên hủy khi đang chờ duyệt; hạn duyệt vẫn còn nên không phải (b)
        var booking = Pending(b =>
        {
            b.Status = BookingStatus.CANCELLED;
            b.CancelReason = reason;
        });

        Assert.Equal(ErrorCodes.BookingNotPendingApproval, Code(booking));
    }

    // ── Thiếu mã quyết định (Q10; DB thật cấm trạng thái này, chỉ kiểm được ở unit) ──

    [Fact]
    public void PendingWithoutDecisionId_IsDecisionNotFound()
    {
        Assert.Equal(ErrorCodes.ApprovalDecisionNotFound, Code(Pending(b => b.TadssDecisionId = null)));
    }

    [Fact]
    public void PendingWithoutApprovalExpiry_IsDecisionNotFound()
    {
        // Dữ liệu lệch: không biết hạn thì không tự coi là còn hạn
        Assert.Equal(ErrorCodes.ApprovalDecisionNotFound, Code(Pending(b => b.ApprovalExpiresAt = null)));
    }

    // ── Exception trả về ───────────────────────────────────────────────────

    [Fact]
    public void Conflicts_Are409WithVietnameseMessage()
    {
        var error = ApprovalConflictResolver.Resolve(Pending(b => b.ApprovalExpiresAt = Now), Now)!;

        Assert.Equal(409, error.StatusCode);
        Assert.Equal("Đơn đã hết hạn duyệt và sẽ được hệ thống hủy tự động.", error.Message);
        Assert.Null(error.Errors);
    }

    [Fact]
    public void NowNotUtc_Throws()
    {
        Assert.Throws<ArgumentException>(() => ApprovalConflictResolver.Resolve(Pending(), DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }
}
