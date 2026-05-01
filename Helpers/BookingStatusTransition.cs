using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Helper để validate state transitions cho BookingStatus
    /// Đảm bảo không có invalid state transitions
    /// </summary>
    public static class BookingStatusTransition
    {
        /// <summary>
        /// Kiểm tra xem có thể chuyển từ status này sang status khác không
        /// </summary>
        public static bool CanTransition(BookingStatus from, BookingStatus to)
        {
            return (from, to) switch
            {
                // Online booking flow
                (BookingStatus.PENDING, BookingStatus.PAID_ONLINE) => true,
                (BookingStatus.PENDING, BookingStatus.CANCELLED) => true,
                
                // Walk-in booking flow
                (BookingStatus.CONFIRMED, BookingStatus.IN_PROGRESS) => true,
                (BookingStatus.CONFIRMED, BookingStatus.CANCELLED) => true,
                (BookingStatus.CONFIRMED, BookingStatus.NO_SHOW) => true,
                
                // After payment
                (BookingStatus.PAID_ONLINE, BookingStatus.IN_PROGRESS) => true,
                (BookingStatus.PAID_ONLINE, BookingStatus.CANCELLED_PENDING_REFUND) => true,
                (BookingStatus.PAID_ONLINE, BookingStatus.NO_SHOW) => true,
                (BookingStatus.PAID_ONLINE, BookingStatus.COMPLETED) => true,  // Không đến nhưng hết giờ
                
                // During play
                (BookingStatus.IN_PROGRESS, BookingStatus.PENDING_PAYMENT) => true,
                (BookingStatus.IN_PROGRESS, BookingStatus.COMPLETED) => true,
                
                // Payment pending
                (BookingStatus.PENDING_PAYMENT, BookingStatus.COMPLETED) => true,
                
                // Cancellation flow
                (BookingStatus.CANCELLED_PENDING_REFUND, BookingStatus.CANCELLED_REFUNDED) => true,
                
                // Default: không cho phép
                _ => false
            };
        }

        /// <summary>
        /// Validate và throw exception nếu transition không hợp lệ
        /// </summary>
        public static void ValidateTransition(BookingStatus from, BookingStatus to)
        {
            if (!CanTransition(from, to))
            {
                throw new InvalidOperationException(
                    $"Invalid booking status transition: {from} -> {to}");
            }
        }

        /// <summary>
        /// Lấy danh sách các status được coi là "active" (chưa hoàn thành/hủy)
        /// </summary>
        public static BookingStatus[] GetActiveStatuses()
        {
            return new[]
            {
                BookingStatus.PENDING,
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE,
                BookingStatus.IN_PROGRESS,
                BookingStatus.PENDING_PAYMENT
            };
        }

        /// <summary>
        /// Lấy danh sách các status đủ điều kiện để check NO_SHOW
        /// </summary>
        public static BookingStatus[] GetNoShowEligibleStatuses()
        {
            return new[]
            {
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE
            };
        }
    }
}
