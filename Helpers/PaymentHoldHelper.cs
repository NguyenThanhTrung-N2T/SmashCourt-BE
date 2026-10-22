using SmashCourt_BE.Configurations;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Hạn giữ chỗ của đơn online (spec Phase 6, 7.5, P6-02). Hàm thuần: nhận "bây giờ" và cấu hình làm tham số, không đọc
    /// đồng hồ, không đọc DB. Mỗi thời điểm của bảng 7.5 là một hàm riêng; 6A-2 chỉ có dòng "đặt sân" (A7), dòng duyệt
    /// (6A-3), IPN thất bại và thanh toán lại (6A-4) thêm sau mà không đổi hàm đã có.
    /// </summary>
    public static class PaymentHoldHelper
    {
        /// <summary>
        /// Dòng "đặt sân": <c>expires_at = now + InitialHoldMinutes</c>, <c>hold_deadline = now + PaymentHoldMaxMinutes</c>.
        /// </summary>
        /// <param name="nowUtc">Thời điểm tạo đơn, bắt buộc Kind = Utc</param>
        public static PaymentHoldWindow ForNewBooking(DateTime nowUtc, BookingPaymentSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            EnsureUtc(nowUtc, nameof(nowUtc));

            // BookingPaymentSettingsValidator đã chặn lúc khởi động; kiểm lại để hàm tự giữ bất biến now < expires ≤ deadline
            if (settings.InitialHoldMinutes < 1)
                throw new ArgumentOutOfRangeException(nameof(settings),
                    $"{nameof(BookingPaymentSettings.InitialHoldMinutes)} phải ≥ 1 (hiện {settings.InitialHoldMinutes}).");
            if (settings.PaymentHoldMaxMinutes < settings.InitialHoldMinutes)
                throw new ArgumentOutOfRangeException(nameof(settings),
                    $"{nameof(BookingPaymentSettings.PaymentHoldMaxMinutes)} ({settings.PaymentHoldMaxMinutes}) phải ≥ " +
                    $"{nameof(BookingPaymentSettings.InitialHoldMinutes)} ({settings.InitialHoldMinutes}).");

            return new PaymentHoldWindow(
                nowUtc.AddMinutes(settings.InitialHoldMinutes),
                nowUtc.AddMinutes(settings.PaymentHoldMaxMinutes));
        }

        private static void EnsureUtc(DateTime value, string paramName)
        {
            if (value.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Mốc thời gian phải có Kind = Utc (spec 7.14).", paramName);
        }
    }
}
