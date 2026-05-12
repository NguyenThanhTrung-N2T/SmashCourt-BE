using SmashCourt_BE.DTOs.Loyalty;

namespace SmashCourt_BE.DTOs.Profile;

/// <summary>
/// DTO cho GET /api/me - Profile của user hiện tại
/// Response khác nhau tùy theo role
/// </summary>
public class UserProfileDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    public bool Is2faEnabled { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; }

    // Chỉ có khi role = CUSTOMER
    public LoyaltyInfo? Loyalty { get; set; }

    // Chỉ có khi role = STAFF/BRANCH_MANAGER
    public BranchInfo? Branch { get; set; }
}

/// <summary>
/// Thông tin chi nhánh của STAFF/MANAGER
/// </summary>
public class BranchInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Role { get; set; } = null!; // STAFF or MANAGER
}
