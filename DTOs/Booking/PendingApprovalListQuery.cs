using SmashCourt_BE.Common;

namespace SmashCourt_BE.DTOs.Booking
{
    /// <summary>
    /// Query của <c>GET /api/bookings/pending-approval</c>. OWNER: bỏ trống = mọi chi nhánh;
    /// BRANCH_MANAGER: bỏ trống hoặc đúng chi nhánh được gán, khác ⇒ 403.
    /// </summary>
    public class PendingApprovalListQuery : PaginationQuery
    {
        public Guid? BranchId { get; set; }
    }
}
