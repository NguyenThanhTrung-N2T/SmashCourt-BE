using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Đếm lịch sử của khách gửi TADSS theo bảng lý do hủy (spec Phase 6, 4.4.1), cùng quy tắc với
    /// <c>export_smashcourt_bookings.sql</c> của Phase 5. Hàm thuần: không đọc DB, không đọc đồng hồ.
    /// </summary>
    public static class CustomerHistoryCalculator
    {
        /// <summary>
        /// Phân loại các booking <b>đã kết thúc trước</b> <paramref name="requestedAtUtc"/> (so nghiêm ngặt).
        /// COMPLETED/NO_SHOW kết thúc lúc giờ kết thúc muộn nhất của các sân; đơn hủy kết thúc lúc <c>cancelled_at</c>.
        /// </summary>
        /// <param name="requestedAtUtc">Thời điểm tạo booking đang xét (UTC), không phải "bây giờ" nên kết quả tất định.</param>
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

                        // APPROVAL_EXPIRED, APPROVAL_REJECTED, POLICY_REJECTED, STAFF_CANCELLED, SYSTEM_CLEANUP và NULL: không tính
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

        // Khách tự hủy mà đã trả tiền và không được hoàn đủ (kể cả chưa có bản ghi hoàn = 0 %) là hủy muộn; chưa trả tiền thì không mất gì
        private static bool IsLateCancellation(CustomerHistoryEntry entry) =>
            entry.HasSuccessfulPayment && (entry.MaxRefundPercent ?? 0m) < 100m;

        private static DateTime? GetPlayEndedAtUtc(CustomerHistoryEntry entry) =>
            entry.LastPlayDate is { } date && entry.LastPlayEndTime is { } endTime
                ? DateTimeHelper.ToUtcFromVietnam(date, endTime)
                : null;

        // Npgsql trả Kind = Utc; Unspecified (dữ liệu dựng tay) được hiểu là UTC thay vì giờ máy
        private static DateTime ToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
