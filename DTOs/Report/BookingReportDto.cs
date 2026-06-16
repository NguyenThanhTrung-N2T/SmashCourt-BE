namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO báo cáo booking
/// </summary>
public class BookingReportDto
{
    public int TotalBookings { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int NoShow { get; set; }
    public int PendingPayment { get; set; }
    public int OnlineBookings { get; set; }
    public int WalkInBookings { get; set; }
    public decimal CancellationRate { get; set; }
    public decimal NoShowRate { get; set; }
    public List<BookingItemDto> Items { get; set; } = [];
}

/// <summary>
/// Chi tiết booking theo nhóm
/// </summary>
public class BookingItemDto
{
    public string Period { get; set; } = null!;
    public int BookingCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
}
