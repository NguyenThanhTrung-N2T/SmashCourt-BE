using System.ComponentModel.DataAnnotations;

namespace SmashCourt_BE.DTOs.Report;

/// <summary>
/// DTO filter chung cho tất cả reports
/// </summary>
public class ReportFilterDto
{
    /// <summary>
    /// Ngày bắt đầu (default: 30 ngày trước)
    /// </summary>
    public DateOnly? FromDate { get; set; }
    
    /// <summary>
    /// Ngày kết thúc (default: hôm nay)
    /// </summary>
    public DateOnly? ToDate { get; set; }
    
    /// <summary>
    /// Chi nhánh (chỉ OWNER sử dụng, BRANCH_MANAGER tự động lấy chi nhánh của mình)
    /// </summary>
    public Guid? BranchId { get; set; }
    
    /// <summary>
    /// Nhóm dữ liệu theo: day, week, month, branch, courtType, paymentMethod, hour, dayOfWeek
    /// </summary>
    [StringLength(50)]
    public string? GroupBy { get; set; }
}
