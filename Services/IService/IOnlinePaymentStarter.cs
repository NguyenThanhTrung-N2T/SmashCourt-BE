using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.IService
{
    public interface IOnlinePaymentStarter
    {
        // Tạo link qua cổng của phương thức và ghi Payment PENDING cho hóa đơn; trả URL thanh toán. Chạy trong transaction của caller
        Task<string> StartAsync(
            Guid invoiceId, string reference, decimal amount, string orderInfo, PaymentTxMethod method, DateTime nowUtc);
    }
}
