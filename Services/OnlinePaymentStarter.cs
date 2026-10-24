using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Một nơi tạo giao dịch online (đặt sân, thanh toán lại) cho mọi cổng: link lấy từ cổng, Payment ghi đúng phương thức.
/// </summary>
public class OnlinePaymentStarter : IOnlinePaymentStarter
{
    private readonly IPaymentGatewayResolver _gateways;
    private readonly IPaymentRepository _paymentRepo;

    public OnlinePaymentStarter(IPaymentGatewayResolver gateways, IPaymentRepository paymentRepo)
    {
        _gateways = gateways;
        _paymentRepo = paymentRepo;
    }

    public async Task<string> StartAsync(
        Guid invoiceId, string reference, decimal amount, string orderInfo, PaymentTxMethod method, DateTime nowUtc)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Số tiền thanh toán online phải > 0.");

        var link = _gateways.Get(method).CreatePaymentLink(reference, amount, orderInfo);
        await _paymentRepo.CreateAsync(new Payment
        {
            InvoiceId = invoiceId,
            Method = method,
            Amount = amount,
            Status = PaymentTxStatus.PENDING,
            TransactionRef = link.TransactionRef,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });
        return link.Url;
    }
}
