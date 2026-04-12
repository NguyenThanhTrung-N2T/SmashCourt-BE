using Microsoft.AspNetCore.Http;

namespace SmashCourt_BE.Services.IService
{
    public interface IPaymentService
    {
        Task HandleVnPayIpnAsync(IQueryCollection query, HttpRequest request);
    }
}
