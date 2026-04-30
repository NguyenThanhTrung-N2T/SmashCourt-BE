namespace SmashCourt_BE.DTOs.Email;

/// <summary>
/// DTO chứa tất cả thông tin cần thiết để gửi email xác nhận booking
/// Tách biệt khỏi Entity để tránh coupling với EF và dễ dàng serialize cho queue
/// </summary>
public class BookingEmailModel
{
    // Thông tin người nhận
    public string Email { get; set; } = null!;
    public string Name { get; set; } = null!;

    // Thông tin chi nhánh
    public string BranchName { get; set; } = null!;
    public string BranchAddress { get; set; } = null!;
    public string BranchPhone { get; set; } = null!;

    // Thông tin đặt sân
    public List<string> CourtNames { get; set; } = new();
    public string BookingDate { get; set; } = null!;
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public string BookingCode { get; set; } = null!;

    // Thông tin thanh toán
    public string TotalAmount { get; set; } = null!;
    public string CourtFee { get; set; } = null!;
    public string? LoyaltyDiscount { get; set; }
    public string? PromotionDiscount { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public string PaymentStatus { get; set; } = null!;

    // Link hủy booking
    public string CancelUrl { get; set; } = null!;
}
