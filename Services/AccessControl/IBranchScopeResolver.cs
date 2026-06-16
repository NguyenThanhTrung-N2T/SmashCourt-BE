using System;
using System.Threading.Tasks;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.AccessControl
{
    public interface IBranchScopeResolver
    {
        /// <summary>
        /// Giải quyết và xác định chi nhánh bắt buộc cho các thao tác nghiệp vụ (CRUD, quản lý sân, quản lý dịch vụ).
        /// </summary>
        /// <param name="requestedBranchId">ID chi nhánh do client yêu cầu (nếu có).</param>
        /// <param name="currentUserId">ID người dùng hiện tại đang đăng nhập.</param>
        /// <param name="currentUserRole">Vai trò của người dùng hiện tại (Enum).</param>
        /// <returns>Trả về Guid đại diện cho ID chi nhánh hợp lệ và được phân quyền.</returns>
        /// <exception cref="AppException">
        /// - 400 (BadRequest) nếu không truyền chi nhánh đối với Owner/Customer.
        /// - 403 (Forbidden) nếu Manager/Staff chưa được gán chi nhánh hoặc cố tình thao tác chi nhánh khác.
        /// </exception>
        Task<Guid> ResolveRequiredBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, UserRole currentUserRole);
        
        /// <summary>
        /// Giải quyết và xác định chi nhánh không bắt buộc (dành cho Báo cáo, Thống kê, Dashboard).
        /// Cho phép OWNER xem toàn bộ hệ thống (trả về null).
        /// </summary>
        /// <param name="requestedBranchId">ID chi nhánh do client yêu cầu (nếu có).</param>
        /// <param name="currentUserId">ID người dùng hiện tại đang đăng nhập.</param>
        /// <param name="currentUserRole">Vai trò của người dùng hiện tại (Enum).</param>
        /// <returns>Trả về Guid? (null đại diện cho tất cả chi nhánh của Owner, hoặc Guid cụ thể).</returns>
        /// <exception cref="AppException">Tương tự ResolveRequiredBranchIdAsync đối với các vai trò khác Owner.</exception>
        Task<Guid?> ResolveOptionalBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, UserRole currentUserRole);
        
        /// <summary>
        /// Phương thức cũ hỗ trợ tương thích ngược.
        /// </summary>
        [Obsolete("Use ResolveRequiredBranchIdAsync or ResolveOptionalBranchIdAsync instead")]
        Task<Guid> ResolveBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, UserRole currentUserRole);
    }
}
