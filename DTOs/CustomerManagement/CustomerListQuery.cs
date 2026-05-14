using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.CustomerManagement;

/// <summary>
/// Query parameters cho danh sách khách hàng
/// </summary>
public class CustomerListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    
    /// <summary>
    /// Tìm kiếm theo tên, SĐT, email
    /// </summary>
    public string? SearchTerm { get; set; }
    
    /// <summary>
    /// Filter theo hạng loyalty
    /// </summary>
    public string? LoyaltyTier { get; set; }
    
    /// <summary>
    /// Filter theo hạng loyalty bằng TierId (ưu tiên hơn LoyaltyTier nếu có)
    /// </summary>
    public Guid? LoyaltyTierId { get; set; }
    
    /// <summary>
    /// Filter theo trạng thái: ACTIVE, LOCKED
    /// </summary>
    public UserStatus? Status { get; set; }
    
    /// <summary>
    /// Filter theo chi nhánh (chỉ OWNER sử dụng)
    /// BRANCH_MANAGER tự động filter theo chi nhánh của mình
    /// </summary>
    public Guid? BranchId { get; set; }
    
    /// <summary>
    /// Sắp xếp theo: fullname, createdat
    /// </summary>
    public string SortBy { get; set; } = "createdat";
    
    /// <summary>
    /// Thứ tự sắp xếp: asc, desc
    /// </summary>
    public string SortOrder { get; set; } = "desc";
}
