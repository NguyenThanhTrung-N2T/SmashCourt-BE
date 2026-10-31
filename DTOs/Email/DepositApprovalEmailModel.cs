namespace SmashCourt_BE.DTOs.Email;

/// <summary>
/// Dữ liệu email của luồng duyệt cọc (đang chờ duyệt, đã duyệt, bị từ chối), đã định dạng sẵn; giờ là giờ VN <c>HH:mm dd/MM/yyyy</c>.
/// Cố ý không có link VNPay và lý do của Quản lý.
/// </summary>
public class DepositApprovalEmailModel
{
    public string Email { get; set; } = null!;
    public string Name { get; set; } = null!;

    public string BranchName { get; set; } = null!;
    public string BranchAddress { get; set; } = null!;
    public string BranchPhone { get; set; } = null!;

    public List<string> CourtNames { get; set; } = new();
    public string BookingDate { get; set; } = null!;
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public string BookingCode { get; set; } = null!;

    public string TotalAmount { get; set; } = null!;
    /// <summary>Tiền cọc phải trả online; null khi chưa có mức cọc</summary>
    public string? DepositAmount { get; set; }
    /// <summary>Phần còn lại thu tại quầy; null khi chưa có mức cọc</summary>
    public string? RemainingAmount { get; set; }
    /// <summary>Hạn thanh toán cọc; null khi đơn không còn hạn thanh toán online</summary>
    public string? PayBefore { get; set; }
    /// <summary>Hạn Quản lý duyệt</summary>
    public string? ApprovalExpiresAt { get; set; }

    /// <summary>Lịch sử đặt sân của khách trên FE (<c>{FrontendBaseUrl}/bookings</c>), nơi bấm "Thanh toán cọc"</summary>
    public string BookingsUrl { get; set; } = null!;
}
