namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// DTO trả về thông tin user cơ bản (dùng cho danh sách)
/// </summary>
public class UserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Chi nhánh hiện tại (nếu có)
    /// </summary>
    public BranchInfoDto? CurrentBranch { get; set; }
}

/// <summary>
/// Thông tin chi nhánh đơn giản
/// </summary>
public class BranchInfoDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Role { get; set; } = null!; // MANAGER hoặc STAFF
}
