using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Services.IService
{
    public interface IPaymentGatewayResolver
    {
        // Cổng của phương thức thanh toán; phương thức chưa đăng ký cổng nào là lỗi cấu hình
        IPaymentGateway Get(PaymentTxMethod method);
    }
}
