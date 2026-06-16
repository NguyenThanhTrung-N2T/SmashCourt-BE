namespace SmashCourt_BE.DTOs.CustomerManagement;

/// <summary>
/// DTO trả về thông tin chi tiết khách hàng
/// OWNER: Xem toàn bộ thông tin
/// BRANCH_MANAGER: Xem thông tin giới hạn
/// </summary>
public class CustomerDetailDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    
    /// <summary>
    /// Email - chỉ OWNER mới thấy
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// Phương thức đăng ký: Email, Google, Facebook - chỉ OWNER mới thấy
    /// </summary>
    public string? RegistrationMethod { get; set; }
    
    /// <summary>
    /// Hạng loyalty: Bronze, Silver, Gold, Platinum, Diamond
    /// </summary>
    public string LoyaltyTier { get; set; } = null!;
    
    /// <summary>
    /// Tổng điểm tích lũy - chỉ OWNER mới thấy
    /// </summary>
    public int? TotalPoints { get; set; }
    
    /// <summary>
    /// Điểm cần thêm để lên hạng tiếp theo - chỉ OWNER mới thấy
    /// </summary>
    public int? PointsToNextTier { get; set; }
    
    /// <summary>
    /// % giảm giá hiện tại từ loyalty
    /// </summary>
    public decimal CurrentDiscount { get; set; }
    
    /// <summary>
    /// Trạng thái tài khoản: ACTIVE, LOCKED
    /// </summary>
    public string Status { get; set; } = null!;
    
    /// <summary>
    /// Thông tin khóa tài khoản (nếu LOCKED)
    /// </summary>
    public LockInfoDto? LockInfo { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Thống kê - OWNER xem toàn hệ thống, MANAGER xem chi nhánh mình
    /// </summary>
    public CustomerStatisticsDto Statistics { get; set; } = null!;
}

/// <summary>
/// Thông tin khóa tài khoản
/// </summary>
public class LockInfoDto
{
    public string? Reason { get; set; }
    public DateTime? LockedAt { get; set; }
    public Guid? LockedBy { get; set; }
    public string? LockedByName { get; set; }
}

/// <summary>
/// Thống kê khách hàng
/// </summary>
public class CustomerStatisticsDto
{
    /// <summary>
    /// Tổng số đơn COMPLETED (toàn hệ thống cho OWNER, chi nhánh cho MANAGER)
    /// </summary>
    public int TotalCompletedBookings { get; set; }
    
    /// <summary>
    /// Tổng doanh thu đóng góp - chỉ OWNER mới thấy
    /// </summary>
    public decimal? TotalRevenue { get; set; }
    
    /// <summary>
    /// Chi nhánh hay đặt nhất - chỉ OWNER mới thấy
    /// </summary>
    public string? MostBookedBranch { get; set; }
    
    /// <summary>
    /// Khung giờ hay đặt nhất - chỉ OWNER mới thấy
    /// </summary>
    public string? MostBookedTimeSlot { get; set; }
    
    /// <summary>
    /// Ngày đặt sân gần nhất
    /// </summary>
    public DateTime? LastBookingDate { get; set; }
}
