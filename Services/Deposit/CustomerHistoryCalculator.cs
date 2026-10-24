using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Services.Deposit;

/// <summary>
/// Đếm lịch sử của khách gửi TADSS theo lý do hủy. Hàm thuần, cùng quy tắc với bản export dữ liệu dùng để đánh giá TADSS.
/// </summary>
public static class CustomerHistoryCalculator
{
    // Chỉ đếm đơn đã kết thúc trước requestedAtUtc (giờ tạo đơn đang xét, không phải "bây giờ", nên kết quả tất định)
    public static CustomerHistorySummary Calculate(IEnumerable<CustomerHistoryEntry> entries, DateTime requestedAtUtc)
    {
        if (requestedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("requestedAtUtc phải có Kind = Utc.", nameof(requestedAtUtc));

        int completed = 0, noShow = 0, cancelled = 0, lateCancelled = 0, paymentExpired = 0;
        DateTime? lastCompletedAt = null;

        foreach (var entry in entries)
        {
            switch (entry.Status)
            {
                case BookingStatus.COMPLETED:
                case BookingStatus.NO_SHOW:
                    var playEnded = GetPlayEndedAtUtc(entry);
                    if (playEnded == null || playEnded.Value >= requestedAtUtc)
                        break;

                    if (entry.Status == BookingStatus.COMPLETED)
                    {
                        completed++;
                        if (lastCompletedAt == null || playEnded.Value > lastCompletedAt.Value)
                            lastCompletedAt = playEnded;
                    }
                    else
                    {
                        noShow++;
                    }

                    break;

                case BookingStatus.CANCELLED:
                case BookingStatus.CANCELLED_PENDING_REFUND:
                case BookingStatus.CANCELLED_REFUNDED:
                    if (entry.CancelledAt == null || ToUtc(entry.CancelledAt.Value) >= requestedAtUtc)
                        break;

                    // Chỉ lỗi của khách được tính; hủy do chi nhánh, hệ thống hoặc TADSS từ chối thì không
                    switch (entry.CancelReason)
                    {
                        case BookingCancelReason.PAYMENT_EXPIRED:
                            paymentExpired++;
                            break;
                        case BookingCancelReason.CUSTOMER_CANCELLED:
                            if (IsLateCancellation(entry))
                                lateCancelled++;
                            else
                                cancelled++;
                            break;
                    }

                    break;
            }
        }

        return new CustomerHistorySummary(completed, noShow, cancelled, lateCancelled, paymentExpired, lastCompletedAt);
    }

    // Đã trả tiền mà không được hoàn đủ là hủy muộn; chưa trả tiền thì khách không mất gì
    private static bool IsLateCancellation(CustomerHistoryEntry entry) =>
        entry.HasSuccessfulPayment && (entry.MaxRefundPercent ?? 0m) < 100m;

    private static DateTime? GetPlayEndedAtUtc(CustomerHistoryEntry entry) =>
        entry.LastPlayDate is { } date && entry.LastPlayEndTime is { } endTime
            ? DateTimeHelper.ToUtcFromVietnam(date, endTime)
            : null;

    // Npgsql trả Kind = Utc; Unspecified được hiểu là UTC thay vì giờ máy
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
