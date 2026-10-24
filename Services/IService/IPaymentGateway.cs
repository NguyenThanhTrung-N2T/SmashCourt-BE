using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;

namespace SmashCourt_BE.Services.IService
{
    // Một cổng thanh toán online: chỉ tạo link và đọc kết quả giao dịch, không chứa nghiệp vụ đặt sân
    public interface IPaymentGateway
    {
        PaymentTxMethod Method { get; }

        // Tạo link thanh toán cho số tiền VND; orderInfo không dấu
        GatewayPaymentLink CreatePaymentLink(string reference, decimal amount, string orderInfo);

        // Kiểm chữ ký và chuẩn hóa kết quả cổng gửi về (IPN hoặc trình duyệt quay lại)
        GatewayPaymentResult ReadResult(IQueryCollection query);
    }
}
