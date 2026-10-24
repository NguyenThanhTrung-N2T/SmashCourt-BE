using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingDepositService
    {
        // Dựng request từ booking đã ghi rồi hỏi TADSS; trả nguyên kết quả. Không ghi DB, không đọc cờ EnableTadssDecision
        Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(Guid bookingId, CancellationToken cancellationToken = default);

        // Chọn nhánh xử lý đơn online: cờ tắt thì không gọi TADSS; mọi lỗi TADSS thành phương án dự phòng, không ném. Không ghi DB
        Task<DepositDecision> DecideAsync(Guid bookingId, decimal finalTotal, CancellationToken cancellationToken = default);
    }
}
