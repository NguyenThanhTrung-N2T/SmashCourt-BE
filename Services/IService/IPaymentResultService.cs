using SmashCourt_BE.Models.Payments;

namespace SmashCourt_BE.Services.IService
{
    public interface IPaymentResultService
    {
        // Áp kết quả giao dịch của mọi cổng: thành công, thất bại (giữ đơn trong ân hạn), tiền về sau khi hủy, trả hai lần.
        // Gọi lặp với cùng kết quả không ghi thêm gì
        Task ApplyAsync(GatewayPaymentResult result);
    }
}
