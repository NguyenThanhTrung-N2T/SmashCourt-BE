using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingDepositService
    {
        // Dựng request quyết định cọc từ booking đã ghi rồi hỏi TADSS; trả nguyên kết quả (thành công hoặc lỗi) cho caller quyết nhánh.
        // Không ghi DB, không đọc cờ EnableTadssDecision (caller kiểm cờ). Booking không tồn tại hoặc thiếu khách/hóa đơn là lỗi lập trình (InvalidOperationException).
        Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(Guid bookingId, CancellationToken cancellationToken = default);

        // Chọn nhánh a/a′/b/c/d (spec 7.6): đọc cờ EnableTadssDecision qua IOptionsMonitor mỗi lần gọi; tắt thì không gọi TADSS,
        // bật thì EvaluateAsync rồi DepositDecisionResolver. Ghi log theo mức quyết định đề xuất; không ghi DB. Caller hủy token ⇒ OperationCanceledException.
        Task<DepositDecision> DecideAsync(Guid bookingId, decimal finalTotal, CancellationToken cancellationToken = default);
    }
}
