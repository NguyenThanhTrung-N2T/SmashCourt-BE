using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;

namespace SmashCourt_BE.Services.IService
{
    // Một cổng thanh toán online: chỉ tạo link và đọc kết quả giao dịch, không chứa nghiệp vụ đặt sân
    public interface IPaymentGateway
    {
        PaymentTxMethod Method { get; }

        // Tạo link thanh toán; link phải hết hạn đúng request.ExpiresAtUtc (P6-27). Cổng không hỗ trợ hạn link thì ném
        // NotSupportedException, không bao giờ tạo link không hạn
        GatewayPaymentLink CreatePaymentLink(GatewayPaymentRequest request);

        // Kiểm chữ ký và chuẩn hóa kết quả cổng gửi về (IPN hoặc trình duyệt quay lại)
        GatewayPaymentResult ReadResult(IQueryCollection query);
    }
}
