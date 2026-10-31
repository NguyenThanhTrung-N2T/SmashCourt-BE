using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Booking đã kết thúc ⇒ outcome gửi TADSS (spec 4.4.1, P6-26). Hàm thuần: cùng booking cho cùng nội dung ở mọi lần gọi, vì TADSS chống trùng
/// outcome theo nội dung (<c>outcome</c> và <c>occurredAt</c> cắt về µs) và gửi lại sau khi mất phản hồi phải nhận 200 bản đã lưu.
/// Nhãn khách đến / không đến dựa trên <c>checked_in_at</c> (dữ liệu trực tiếp, ghi một lần lúc check-in); trạng thái lệch dữ liệu thì
/// không đoán mà trả <see cref="TadssOutcomePlanKind.Invalid"/>.
/// </summary>
public static class TadssOutcomeMapper
{
    // Giờ chơi lưu theo giờ tường của chi nhánh (Việt Nam, không có giờ mùa hè)
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static TadssOutcomePlan Plan(Booking booking, DateTime nowUtc) => booking.Status switch
    {
        // PENDING_PAYMENT: khách đã chơi, chỉ chưa check-out (IPN để hóa đơn PARTIALLY_PAID nên đây là nhánh thường gặp)
        BookingStatus.COMPLETED or BookingStatus.PENDING_PAYMENT => booking.CheckedInAt == null
            ? TadssOutcomePlan.Invalid($"{booking.Status} nhưng checked_in_at trống (không có lần check-in)")
            : PlanPlayed(booking, TadssOutcome.COMPLETED, nowUtc),
        BookingStatus.NO_SHOW => booking.CheckedInAt != null
            ? TadssOutcomePlan.Invalid($"NO_SHOW nhưng checked_in_at có giá trị {booking.CheckedInAt:O} (khách đã check-in)")
            : PlanPlayed(booking, TadssOutcome.NO_SHOW, nowUtc),
        BookingStatus.CANCELLED or BookingStatus.CANCELLED_PENDING_REFUND or BookingStatus.CANCELLED_REFUNDED => PlanCancelled(booking),
        _ => TadssOutcomePlan.NotDue
    };

    // occurredAt = giờ kết thúc muộn nhất của đơn; check-out sớm vẫn chờ tới giờ đó để occurredAt không nằm ở tương lai
    private static TadssOutcomePlan PlanPlayed(Booking booking, TadssOutcome outcome, DateTime nowUtc)
    {
        if (booking.BookingCourts.Count == 0)
            return TadssOutcomePlan.Invalid($"{booking.Status} nhưng đơn không có sân nào");

        // Mỗi đơn một ngày chơi, giờ kết thúc luôn sau giờ bắt đầu: không có khung qua nửa đêm
        var last = booking.BookingCourts.OrderByDescending(bc => bc.Date).ThenByDescending(bc => bc.EndTime).First();
        var playEnd = new DateTimeOffset(last.Date.ToDateTime(last.EndTime), VietnamOffset);

        return playEnd.UtcDateTime > nowUtc ? TadssOutcomePlan.NotDue : TadssOutcomePlan.Send(outcome, playEnd);
    }

    private static TadssOutcomePlan PlanCancelled(Booking booking)
    {
        if (booking.CancelReason is not { } reason)
            return TadssOutcomePlan.Invalid($"{booking.Status} nhưng cancel_reason trống");
        if (booking.CancelledAt is not { } cancelledAt)
            return TadssOutcomePlan.Invalid($"{booking.Status} nhưng cancelled_at trống");

        var outcome = reason switch
        {
            BookingCancelReason.PAYMENT_EXPIRED => TadssOutcome.PAYMENT_EXPIRED,
            BookingCancelReason.APPROVAL_EXPIRED => TadssOutcome.APPROVAL_EXPIRED,
            BookingCancelReason.APPROVAL_REJECTED or BookingCancelReason.POLICY_REJECTED => TadssOutcome.REJECTED,
            BookingCancelReason.CUSTOMER_CANCELLED => TadssOutcome.CANCELLED_BY_CUSTOMER,
            BookingCancelReason.STAFF_CANCELLED => TadssOutcome.CANCELLED_BY_STAFF,
            _ => (TadssOutcome?)null
        };

        // SYSTEM_CLEANUP (BE dừng giữa hai transaction): không phải kết quả của khách, spec không gửi
        return outcome is { } value ? TadssOutcomePlan.Send(value, new DateTimeOffset(AsUtc(cancelledAt))) : TadssOutcomePlan.Skip;
    }

    // Npgsql trả Kind = Utc; Unspecified được hiểu là UTC thay vì giờ máy
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
