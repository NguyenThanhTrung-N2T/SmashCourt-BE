namespace SmashCourt_BE.DTOs.CustomerManagement;

/// <summary>
/// DTO trả về lịch sử tích điểm loyalty (chỉ OWNER xem được)
/// </summary>
public class LoyaltyTransactionDto
{
    public Guid TransactionId { get; set; }
    public Guid? BookingId { get; set; }
    public int Points { get; set; }
    public int TotalPointsAfter { get; set; }
    public string Type { get; set; } = null!; // EARNED, REDEEMED, ADJUSTED
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Query parameters cho lịch sử loyalty
/// </summary>
public class LoyaltyTransactionQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    
    /// <summary>
    /// Filter từ ngày
    /// </summary>
    public DateTime? FromDate { get; set; }
    
    /// <summary>
    /// Filter đến ngày
    /// </summary>
    public DateTime? ToDate { get; set; }
}
