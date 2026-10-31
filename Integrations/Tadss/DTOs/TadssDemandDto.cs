namespace SmashCourt_BE.Integrations.Tadss.DTOs;

/// <summary>Tín hiệu nhu cầu tại thời điểm tạo booking (tùy chọn).</summary>
public class TadssDemandDto
{
    /// <summary>Tỷ lệ lấp đầy trong [0, 1].</summary>
    public required decimal OccupancyRate { get; set; }
    public required int WaitlistCount { get; set; }
}
