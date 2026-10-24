using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingApprovalService
    {
        // Hàng chờ duyệt cọc đọc từ DB của BE (không gọi TADSS), phạm vi chi nhánh theo vai trò
        Task<PagedResult<PendingApprovalBookingDto>> GetPendingApprovalsAsync(PendingApprovalListQuery query, Guid userId, string role);

        // Duyệt đơn chờ duyệt kèm tỷ lệ cọc qua TADSS; lỗi TADSS không bao giờ thành cọc 100 %
        Task<BookingApprovalResultDto> ApproveAsync(Guid bookingId, ApproveBookingDto dto, Guid actorId, string role);

        // Từ chối đơn chờ duyệt qua TADSS: hủy APPROVAL_REJECTED, nhả sân
        Task<BookingApprovalResultDto> RejectAsync(Guid bookingId, RejectBookingDto dto, Guid actorId, string role);
    }
}
