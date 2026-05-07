namespace SmashCourt_BE.DTOs.UserManagement;

/// <summary>
/// DTO trả về thông tin chi tiết user (dùng cho xem chi tiết)
/// </summary>
public class UserDetailDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    public bool IsEmailVerified { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    /// <summary>
    /// Thông tin khóa tài khoản (nếu có)
    /// </summary>
    public LockInfoDto? LockInfo { get; set; }
    
    /// <summary>
    /// Chi nhánh hiện tại (nếu có)
    /// </summary>
    public BranchInfoDto? CurrentBranch { get; set; }
}

/// <summary>
/// Thông tin khóa tài khoản
/// </summary>
public class LockInfoDto
{
    public string? Reason { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? LockedByName { get; set; }
    public Guid? LockedBy { get; set; }
}
