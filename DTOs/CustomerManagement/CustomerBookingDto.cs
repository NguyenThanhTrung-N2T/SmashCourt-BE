namespace SmashCourt_BE.DTOs.CustomerManagement;

/// <summary>
/// DTO trả về lịch sử booking của khách hàng
/// </summary>
public class CustomerBookingDto
{
    public Guid BookingId { get; set; }
    public string BranchName { get; set; } = null!;
    public DateOnly BookingDate { get; set; }
    public string CourtNames { get; set; } = null!; // "A1, A2" nếu đặt nhiều sân
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Query parameters cho lịch sử booking
/// </summary>
public class CustomerBookingQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    
    /// <summary>
    /// Filter theo chi nhánh (chỉ OWNER sử dụng)
    /// </summary>
    public Guid? BranchId { get; set; }
    
    /// <summary>
    /// Filter theo trạng thái booking
    /// </summary>
    public string? Status { get; set; }
    
    /// <summary>
    /// Filter từ ngày
    /// </summary>
    public DateTime? FromDate { get; set; }
    
    /// <summary>
    /// Filter đến ngày
    /// </summary>
    public DateTime? ToDate { get; set; }
}
