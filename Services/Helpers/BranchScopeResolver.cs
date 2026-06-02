using System;
using System.Threading.Tasks;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Services.Helpers
{
    public class BranchScopeResolver : IBranchScopeResolver
    {
        private readonly IUserBranchRepository _userBranchRepo;
        private readonly IBranchRepository _branchRepo;

        public BranchScopeResolver(IUserBranchRepository userBranchRepo, IBranchRepository branchRepo)
        {
            _userBranchRepo = userBranchRepo;
            _branchRepo = branchRepo;
        }

        /// <inheritdoc />
        public async Task<Guid> ResolveRequiredBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, UserRole currentUserRole)
        {
            // OWNER: Bắt buộc chọn chi nhánh (không validate tồn tại - owner có thể xem cả chi nhánh inactive)
            if (currentUserRole == UserRole.OWNER)
            {
                if (!requestedBranchId.HasValue)
                    throw new AppException(400, "Vui lòng chọn chi nhánh", ErrorCodes.BadRequest);
                return requestedBranchId.Value;
            }

            // MANAGER/STAFF: Sử dụng chi nhánh được gán
            if (currentUserRole == UserRole.BRANCH_MANAGER || currentUserRole == UserRole.STAFF)
            {
                var assignment = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
                if (assignment == null)
                    throw new AppException(403, "Bạn chưa được gán vào chi nhánh nào", ErrorCodes.Forbidden);

                if (requestedBranchId.HasValue && requestedBranchId.Value != assignment.BranchId)
                    throw new AppException(403, "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden);

                // Validate branch exists and is active for staff/manager
                var branch = await _branchRepo.GetByIdAsync(assignment.BranchId);
                if (branch == null)
                    throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);
                
                if (branch.Status != BranchStatus.ACTIVE)
                    throw new AppException(400, "Chi nhánh không khả dụng", ErrorCodes.BadRequest);

                return assignment.BranchId;
            }

            // CUSTOMER / Invalid role: Không được phép thao tác Admin API
            throw new AppException(403, "Role không được phép sử dụng chức năng này", ErrorCodes.Forbidden);
        }

        /// <inheritdoc />
        public async Task<Guid?> ResolveOptionalBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, UserRole currentUserRole)
        {
            // OWNER: Cho phép null (xem toàn hệ thống) hoặc chọn chi nhánh cụ thể (không validate)
            if (currentUserRole == UserRole.OWNER)
            {
                return requestedBranchId; // null = all branches, có giá trị = branch cụ thể
            }

            // MANAGER/STAFF: Luôn sử dụng chi nhánh được gán (không bao giờ null)
            if (currentUserRole == UserRole.BRANCH_MANAGER || currentUserRole == UserRole.STAFF)
            {
                var assignment = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
                if (assignment == null)
                    throw new AppException(403, "Bạn chưa được gán vào chi nhánh nào", ErrorCodes.Forbidden);

                if (requestedBranchId.HasValue && requestedBranchId.Value != assignment.BranchId)
                    throw new AppException(403, "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden);

                // Validate branch exists and is active for staff/manager
                var branch = await _branchRepo.GetByIdAsync(assignment.BranchId);
                if (branch == null)
                    throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);
                
                if (branch.Status != BranchStatus.ACTIVE)
                    throw new AppException(400, "Chi nhánh không khả dụng", ErrorCodes.BadRequest);

                return assignment.BranchId;
            }

            // CUSTOMER / Invalid role: Không được phép thao tác Admin API
            throw new AppException(403, "Role không được phép sử dụng chức năng này", ErrorCodes.Forbidden);
        }

        /// <inheritdoc />
        [Obsolete("Use ResolveRequiredBranchIdAsync or ResolveOptionalBranchIdAsync instead")]
        public async Task<Guid> ResolveBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, UserRole currentUserRole)
        {
            return await ResolveRequiredBranchIdAsync(requestedBranchId, currentUserId, currentUserRole);
        }
    }
}
