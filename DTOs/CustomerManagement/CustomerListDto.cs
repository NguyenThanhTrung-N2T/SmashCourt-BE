namespace SmashCourt_BE.DTOs.CustomerManagement;

/// <summary>
/// DTO trả về thông tin khách hàng cơ bản (dùng cho danh sách)
/// </summary>
public class CustomerListDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    
    /// <summary>
    /// Email - chỉ OWNER mới thấy, BRANCH_MANAGER không thấy
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Hạng loyalty: Bronze, Silver, Gold, Platinum, Diamond
    /// </summary>
    public string LoyaltyTier { get; set; } = null!;
    
    /// <summary>
    /// Tổng điểm tích lũy - chỉ OWNER mới thấy
    /// </summary>
    public int? TotalPoints { get; set; }
    
    /// <summary>
    /// Tổng số đơn COMPLETED toàn hệ thống (OWNER) hoặc tại chi nhánh (MANAGER)
    /// </summary>
    public int TotalCompletedBookings { get; set; }
    
    /// <summary>
    /// Trạng thái tài khoản: ACTIVE, LOCKED
    /// </summary>
    public string Status { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; }
}
