using Microsoft.AspNetCore.Authorization;

namespace SmashCourt_BE.Configurations;

/// <summary>
/// Định nghĩa tập trung tất cả Authorization Policies.
/// Dùng: [Authorize(Policy = AuthorizationPolicies.OwnerOnly)]
/// </summary>
public static class AuthorizationPolicies
{
    // ─── Policy names (constants để tránh magic string) ───────────────────

    /// <summary>Chỉ OWNER hệ thống</summary>
    public const string OwnerOnly = "OwnerOnly";

    /// <summary>OWNER hoặc BRANCH_MANAGER — quản lý cấp chi nhánh trở lên</summary>
    public const string OwnerOrManager = "OwnerOrManager";

    /// <summary>Mọi nhân viên: OWNER, BRANCH_MANAGER, STAFF — không bao gồm CUSTOMER</summary>
    public const string StaffAndAbove = "StaffAndAbove";

    /// <summary>Chỉ CUSTOMER — các tính năng dành riêng cho khách hàng</summary>
    public const string CustomerOnly = "CustomerOnly";

    /// <summary>Mọi role đã xác thực (CUSTOMER, STAFF, BRANCH_MANAGER, OWNER)</summary>
    public const string AnyAuthenticated = "AnyAuthenticated";

    // ─── Register ─────────────────────────────────────────────────────────

    /// <summary>
    /// Đăng ký tất cả policies vào AuthorizationOptions.
    /// Gọi trong Program.cs: builder.Services.AddAuthorization(AuthorizationPolicies.Register)
    /// </summary>
    public static void Register(AuthorizationOptions options)
    {
        // Chỉ OWNER
        options.AddPolicy(OwnerOnly, policy =>
            policy.RequireRole("OWNER"));

        // OWNER hoặc BRANCH_MANAGER
        options.AddPolicy(OwnerOrManager, policy =>
            policy.RequireRole("OWNER", "BRANCH_MANAGER"));

        // Mọi nhân viên (không bao gồm CUSTOMER)
        options.AddPolicy(StaffAndAbove, policy =>
            policy.RequireRole("OWNER", "BRANCH_MANAGER", "STAFF"));

        // Chỉ CUSTOMER
        options.AddPolicy(CustomerOnly, policy =>
            policy.RequireRole("CUSTOMER"));

        // Mọi role đã đăng nhập — alias rõ ràng hơn [Authorize] thuần
        options.AddPolicy(AnyAuthenticated, policy =>
            policy.RequireAuthenticatedUser());
    }
}
