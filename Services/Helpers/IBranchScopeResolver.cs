using System;
using System.Threading.Tasks;

namespace SmashCourt_BE.Services.Helpers
{
    public interface IBranchScopeResolver
    {
        Task<Guid> ResolveBranchIdAsync(Guid? requestedBranchId, Guid currentUserId, string currentUserRole);
    }
}
