using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using System.Security.Cryptography;
using System.Text;

namespace SmashCourt_BE.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _service;

        public PaymentController(IPaymentService service)
        {
            _service = service;
        }

        [HttpPost("vnpay/ipn")]
        public async Task<IActionResult> VnPayIpn([FromQuery] IQueryCollection query)
        {
            await _service.HandleVnPayIpnAsync(query, Request);
            // VNPay yêu cầu response đúng format
            return Ok(new { RspCode = "00", Message = "Confirm Success" });
        }
    }
}
