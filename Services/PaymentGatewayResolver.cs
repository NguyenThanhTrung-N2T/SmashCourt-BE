using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Chọn cổng thanh toán theo phương thức trong các <see cref="IPaymentGateway"/> đã đăng ký DI; thêm cổng mới chỉ cần đăng ký thêm.
/// </summary>
public class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IReadOnlyDictionary<PaymentTxMethod, IPaymentGateway> _gateways;

    public PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways)
    {
        _gateways = gateways.ToDictionary(g => g.Method);
    }

    public IPaymentGateway Get(PaymentTxMethod method) =>
        _gateways.TryGetValue(method, out var gateway)
            ? gateway
            : throw new InvalidOperationException($"Chưa đăng ký cổng thanh toán cho phương thức {method}.");
}
