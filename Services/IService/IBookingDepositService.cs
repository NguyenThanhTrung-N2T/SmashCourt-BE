using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingDepositService
    {
        // Dựng request quyết định cọc từ booking đã ghi rồi hỏi TADSS; trả nguyên kết quả (thành công hoặc lỗi) cho caller quyết nhánh.
        // Không ghi DB, không đọc cờ EnableTadssDecision (caller kiểm cờ). Booking không tồn tại hoặc thiếu khách/hóa đơn là lỗi lập trình (InvalidOperationException).
        Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(Guid bookingId, CancellationToken cancellationToken = default);
    }
}
