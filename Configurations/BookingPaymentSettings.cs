namespace SmashCourt_BE.Configurations;

/// <summary>
/// Cấu hình giữ chỗ và thanh toán online — ánh xạ từ appsettings.json section "BookingPayment".
/// </summary>
public class BookingPaymentSettings
{
    public const string SectionName = "BookingPayment";

    /// <summary>Thời gian giữ chỗ ban đầu khi tạo đơn online (phút).</summary>
    public int InitialHoldMinutes { get; set; } = 10;
    /// <summary>Số phút gia hạn mỗi lần thanh toán lại (phút).</summary>
    public int RetryExtendMinutes { get; set; } = 10;
    /// <summary>Ân hạn sau khi thanh toán thất bại (phút), không vượt <see cref="PaymentHoldMaxMinutes"/>.</summary>
    public int PaymentFailureGraceMinutes { get; set; } = 5;
    /// <summary>Hạn giữ chỗ tuyệt đối tính từ lúc tạo đơn (phút).</summary>
    public int PaymentHoldMaxMinutes { get; set; } = 15;
    /// <summary>Số tiền tối thiểu một giao dịch VNPay (VND); cọc thấp hơn thì đơn được xác nhận ngay.</summary>
    public decimal MinOnlinePaymentAmount { get; set; } = 5000m;
}
