using SmashCourt_BE.Configurations;
using SmashCourt_BE.Models.Deposit;

namespace SmashCourt_BE.Services.Deposit;

/// <summary>
/// Hạn thanh toán và hạn giữ chỗ tuyệt đối của đơn online. Hàm thuần: nhận "bây giờ" làm tham số; mọi mốc là UTC.
/// </summary>
public static class PaymentHoldPolicy
{
    // Đặt sân: hạn thanh toán = now + InitialHoldMinutes, hạn giữ chỗ = now + PaymentHoldMaxMinutes
    public static PaymentHoldWindow ForNewBooking(DateTime nowUtc, BookingPaymentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureUtc(nowUtc, nameof(nowUtc));

        // Validator đã chặn lúc khởi động; kiểm lại để hàm tự giữ bất biến now < hạn thanh toán ≤ hạn giữ chỗ
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

    // Duyệt mức cọc > 0: hạn = min(now + số phút, giờ chơi), cũng là hạn giữ chỗ mới. False khi giờ chơi đã tới (không trả mốc đã qua)
    public static bool TryForApproval(
        DateTime nowUtc, DateTime playStartUtc, int approvedWindowMinutes, out PaymentHoldWindow window)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureUtc(playStartUtc, nameof(playStartUtc));
        if (approvedWindowMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(approvedWindowMinutes), approvedWindowMinutes, "Số phút phải ≥ 1.");

        var windowEnd = nowUtc.AddMinutes(approvedWindowMinutes);
        var expiresAt = playStartUtc < windowEnd ? playStartUtc : windowEnd;
        if (expiresAt <= nowUtc)
        {
            window = default;
            return false;
        }

        window = new PaymentHoldWindow(expiresAt, expiresAt);
        return true;
    }

    // Thanh toán lại: hạn = min(hạn giữ chỗ, max(hạn hiện tại, now + số phút)), không rút ngắn. False khi đã tới hạn giữ chỗ
    public static bool TryForRetry(
        DateTime nowUtc, DateTime currentExpiresAtUtc, DateTime holdDeadlineUtc, int retryExtendMinutes, out PaymentHoldWindow window)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureUtc(currentExpiresAtUtc, nameof(currentExpiresAtUtc));
        EnsureUtc(holdDeadlineUtc, nameof(holdDeadlineUtc));
        if (retryExtendMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(retryExtendMinutes), retryExtendMinutes, "Số phút phải ≥ 1.");

        if (nowUtc >= holdDeadlineUtc)
        {
            window = default;
            return false;
        }

        var extended = nowUtc.AddMinutes(retryExtendMinutes);
        var wanted = currentExpiresAtUtc > extended ? currentExpiresAtUtc : extended;
        window = new PaymentHoldWindow(wanted < holdDeadlineUtc ? wanted : holdDeadlineUtc, holdDeadlineUtc);
        return true;
    }

    // Thanh toán thất bại: hạn = min(hạn hiện tại, now + ân hạn); chỉ rút ngắn, không mở lại hạn đã qua
    public static DateTime ForPaymentFailure(DateTime nowUtc, DateTime currentExpiresAtUtc, int graceMinutes)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureUtc(currentExpiresAtUtc, nameof(currentExpiresAtUtc));
        if (graceMinutes < 1)
            throw new ArgumentOutOfRangeException(nameof(graceMinutes), graceMinutes, "Số phút phải ≥ 1.");

        var graceEnd = nowUtc.AddMinutes(graceMinutes);
        return graceEnd < currentExpiresAtUtc ? graceEnd : currentExpiresAtUtc;
    }

    private static void EnsureUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Mốc thời gian phải có Kind = Utc.", paramName);
    }
}
