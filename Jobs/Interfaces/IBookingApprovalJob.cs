namespace SmashCourt_BE.Jobs.Interfaces
{
    public interface IBookingApprovalJob
    {
        // Hủy đơn chờ duyệt quá hạn duyệt (APPROVAL_EXPIRED); không bao giờ tự duyệt
        Task ExpireApprovalsAsync();
    }
}
