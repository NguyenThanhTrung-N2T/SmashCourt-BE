using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// Query parameters để lọc danh sách users
/// </summary>
public class UserListQuery : PaginationQuery
{
    /// <summary>
    /// Tìm kiếm theo tên, email, hoặc số điện thoại
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Lọc theo role
    /// </summary>
    public UserRole? Role { get; set; }

    /// <summary>
    /// Lọc theo status
    /// </summary>
    public UserStatus? Status { get; set; }

    /// <summary>
    /// Lọc theo chi nhánh
    /// </summary>
    public Guid? BranchId { get; set; }

    /// <summary>
    /// Sắp xếp theo (createdAt, fullName, email)
    /// </summary>
    public string SortBy { get; set; } = "createdAt";

    /// <summary>
    /// Thứ tự sắp xếp (asc, desc)
    /// </summary>
    public string SortOrder { get; set; } = "desc";
}
