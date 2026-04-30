using Microsoft.AspNetCore.Mvc;
using SmashCourt_BE.Common;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.DTOs.Payment;

namespace SmashCourt_BE.Controllers
{
    public class VnPayConfirmRequest
    {
        public Dictionary<string, string> QueryParams { get; set; } = new();
    }

    [ApiController]
    [Route("api/payments")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentService _service;
        private readonly IConfiguration _config;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IPaymentService service, 
            IConfiguration config,
            ILogger<PaymentController> logger)
        {
            _service = service;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Build curl command để test confirm endpoint trong development
        /// </summary>
        private string BuildCurlCommand(Dictionary<string, string> queryParams)
        {
            var jsonBody = System.Text.Json.JsonSerializer.Serialize(new { queryParams }, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = false
            });

            // Escape double quotes cho bash
            var escapedJson = jsonBody.Replace("\"", "\\\"");
            
            // Lấy base URL từ request context thay vì hardcode
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            return $@"curl -X POST {baseUrl}/api/payments/vnpay/confirm \
  -H ""Content-Type: application/json"" \
  -d ""{escapedJson}""";
        }

        /// <summary>
        /// VNPay IPN: VNPay gọi server-to-server bằng GET với params trong query string
        /// ✅ Đây là nơi UPDATE DATABASE
        /// </summary>
        [HttpGet("vnpay/ipn")]
        public async Task<IActionResult> VnPayIpn()
        {
            await _service.HandleVnPayIpnAsync(Request.Query, Request);
            return Ok(new { RspCode = "00", Message = "Confirm Success" });
        }

        /// <summary>
        /// VNPay Payment Confirmation: Frontend gọi sau khi nhận callback từ VNPay
        /// ✅ Đây cũng UPDATE DATABASE (vì sandbox không gọi IPN)
        /// Frontend sẽ forward query params từ VNPay về endpoint này
        /// </summary>
        [HttpPost("vnpay/confirm")]
        public async Task<IActionResult> VnPayConfirm([FromBody] VnPayConfirmRequest request)
        {
            // Convert Dictionary sang QueryCollection
            var queryCollection = new QueryCollection(
                request.QueryParams.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new Microsoft.Extensions.Primitives.StringValues(kvp.Value)
                )
            );

            // Xử lý giống IPN
            await _service.HandleVnPayIpnAsync(queryCollection, Request);

            // Trả về kết quả để FE hiển thị
            var result = await _service.HandleVnPayReturnAsync(queryCollection);
            
            return Ok(ApiResponse<VnPayReturnResult>.Ok(result, 
                result.IsSuccess ? "Xác nhận thanh toán thành công" : "Thanh toán thất bại"));
        }

        /// <summary>
        /// VNPay Return URL: Browser redirect sau khi user thanh toán
        /// ✅ REDIRECT về FE với query params để FE xử lý UI
        /// ⚠️ KHÔNG UPDATE DATABASE - chỉ redirect về FE
        /// FE sẽ gọi /confirm endpoint để update DB
        /// </summary>
        [HttpGet("vnpay/callback")]
        public IActionResult VnPayCallback()
        {
            try
            {
                // ===== LOG DEBUG INFO (chỉ trong development) =====
                var queryParams = Request.Query.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToString()
                );
                
                var curlCommand = BuildCurlCommand(queryParams);
                _logger.LogInformation("\n{Separator}\n🔥 VNPAY CALLBACK RECEIVED - Copy lệnh này để test confirm endpoint:\n{Separator}\n{CurlCommand}\n{Separator}\n", 
                    new string('=', 80), new string('=', 80), curlCommand, new string('=', 80));
                // ===== END LOG =====

                // Lấy FE base URL từ config (fallback về localhost cho development)
                var feBaseUrl = _config["FrontendBaseUrl"] ?? "http://localhost:3000";
                var fePaymentResultPath = "/payment/result";
                
                // Build query string với proper URL encoding
                var queryString = string.Join("&", Request.Query.Select(q => 
                    $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value.ToString())}"));
                
                var redirectUrl = $"{feBaseUrl}{fePaymentResultPath}?{queryString}";
                
                _logger.LogInformation("🔄 Redirecting to FE: {RedirectUrl}", redirectUrl);
                
                // Redirect về FE
                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing VNPay callback");
                
                // Fallback: redirect về FE error page
                var feBaseUrl = _config["FrontendBaseUrl"] ?? "http://localhost:3000";
                return Redirect($"{feBaseUrl}/payment/error");
            }
        }
    }
}
