namespace SmashCourt_BE.Services.IService
{
    public class VnPayPaymentUrlResult
    {
        public string Url { get; set; } = null!;
        public string TransactionRef { get; set; } = null!;
    }

    public interface IVnPayService
    {
        VnPayPaymentUrlResult CreatePaymentUrl(string transactionRef, decimal amount, string orderInfo);
        bool VerifyIpn(IQueryCollection query, out string transactionRef,
            out bool isSuccess, out string rawPayload);
    }
}
