namespace SmashCourt_BE.Services.IService
{
    public interface IVnPayService
    {
        string CreatePaymentUrl(string transactionRef, decimal amount, string orderInfo);
        bool VerifyIpn(IQueryCollection query, out string transactionRef,
            out bool isSuccess, out string rawPayload);
    }
}
