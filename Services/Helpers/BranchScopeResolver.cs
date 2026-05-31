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

        public BranchScopeResolver(IUserBranchRepository userBranchRepo)
        {
            _userBranchRepo = userBranchRepo;
        }

        public async Task<Guid> ResolveBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, string currentUserRole)
        {
            var isManagerOrStaff = !string.IsNullOrEmpty(currentUserRole) &&
                (currentUserRole == UserRole.BRANCH_MANAGER.ToString() ||
                 currentUserRole == UserRole.STAFF.ToString());

            if (!isManagerOrStaff)
            {
                if (!requestedBranchId.HasValue)
                    throw new AppException(400, "Vui lòng chọn chi nhánh", ErrorCodes.BadRequest);
                return requestedBranchId.Value;
            }

            var assignment = await _userBranchRepo.GetActiveByUserIdAsync(currentUserId);
            if (assignment == null)
                throw new AppException(403, "Bạn chưa được gán vào chi nhánh nào", ErrorCodes.Forbidden);

            if (requestedBranchId.HasValue && requestedBranchId.Value != assignment.BranchId)
                throw new AppException(403, "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden);

            return assignment.BranchId;
        }
    }
}
