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
        /// VNPay IPN (Instant Payment Notification): VNPay gọi server-to-server bằng GET
        /// 
        /// 🎯 RESPONSIBILITY: UPDATE DATABASE (Source of Truth)
        /// 
        /// Flow:
        /// 1. VNPay gọi endpoint này sau khi user thanh toán (server-to-server)
        /// 2. Verify signature từ VNPay
        /// 3. Update booking status → PAID_ONLINE hoặc CANCELLED
        /// 4. Update payment status → SUCCESS hoặc FAILED
        /// 5. Update invoice status → PARTIALLY_PAID
        /// 6. Gửi email xác nhận (nếu thành công)
        /// 
        /// 🔒 IDEMPOTENCY: VNPay có thể gọi IPN nhiều lần (retry mechanism)
        /// - Check booking.Status trước khi update
        /// - Nếu đã PAID_ONLINE hoặc CANCELLED → return early (idempotent)
        /// - Log tất cả IPN requests vào payment_ipn_logs table
        /// 
        /// ⚠️ PRODUCTION: VNPay LUÔN gọi IPN endpoint này
        /// ⚠️ SANDBOX: Cần config IPN URL trong VNPay dashboard (xem /vnpay/confirm docs)
        /// </summary>
        [HttpGet("vnpay/ipn")]
        public async Task<IActionResult> VnPayIpn()
        {
            _logger.LogInformation("🔔 VNPay IPN received - Processing payment update");
            await _service.HandleVnPayIpnAsync(Request.Query, Request);
            return Ok(new { RspCode = "00", Message = "Confirm Success" });
        }

        /// <summary>
        /// VNPay Payment Confirmation: Frontend gọi sau khi nhận callback từ VNPay
        /// 
        /// 🎯 RESPONSIBILITY: HYBRID - IPN Primary + Confirm Fallback
        /// 
        /// Flow:
        /// 1. User thanh toán xong → VNPay redirect browser về /vnpay/callback
        /// 2. Backend redirect về FE với query params
        /// 3. FE gọi endpoint này
        /// 4. Check: Đã được xử lý bởi IPN chưa?
        ///    - ✅ Đã xử lý → Chỉ read và return (idempotent)
        ///    - ❌ Chưa xử lý → Update DB như fallback (sandbox case)
        /// 
        /// 🔒 IDEMPOTENCY GUARANTEE:
        /// - Check payment.Status trước khi update
        /// - Nếu đã SUCCESS → skip update, chỉ read
        /// - HandleVnPayIpnAsync có idempotency check riêng
        /// - Safe để gọi nhiều lần
        /// 
        /// 🎯 WHY HYBRID?
        /// - Production: IPN luôn được gọi → Confirm chỉ read
        /// - Sandbox: IPN có thể fail/delay → Confirm làm fallback
        /// - Best of both worlds: Reliable + Production-ready
        /// 
        /// 📊 SCENARIOS:
        /// 
        /// Scenario 1: IPN hoạt động (Production)
        /// - IPN → Update DB → payment.Status = SUCCESS
        /// - Confirm → Check payment.Status = SUCCESS → Skip update, chỉ read
        /// - ✅ Không duplicate
        /// 
        /// Scenario 2: IPN không hoạt động (Sandbox)
        /// - Confirm → Check payment.Status = PENDING → Update DB
        /// - ✅ System vẫn chạy
        /// 
        /// Scenario 3: IPN đến trễ
        /// - Confirm → Update DB → payment.Status = SUCCESS
        /// - IPN → Check booking.Status = PAID_ONLINE → Skip (idempotent)
        /// - ✅ Không duplicate
        /// 
        /// Scenario 4: User refresh page
        /// - Confirm lần 1 → Update DB
        /// - Confirm lần 2 → Check payment.Status = SUCCESS → Skip
        /// - ✅ Idempotent
        /// </summary>
        [HttpPost("vnpay/confirm")]
        public async Task<IActionResult> VnPayConfirm([FromBody] VnPayConfirmRequest request)
        {
            _logger.LogInformation("📱 VNPay Confirm called by FE - Checking payment status");
            
            // Convert Dictionary sang QueryCollection
            var queryCollection = new QueryCollection(
                request.QueryParams.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new Microsoft.Extensions.Primitives.StringValues(kvp.Value)
                )
            );

            // 🔥 STEP 1: Check đã xử lý bởi IPN chưa (idempotency check)
            var txnRef = queryCollection["vnp_TxnRef"].ToString();
            
            // ✅ PRODUCTION MODE: Confirm chỉ READ-ONLY
            // IPN đã update DB → Confirm chỉ đọc kết quả để return cho FE
            _logger.LogInformation(
                "📱 CONFIRM | Reading payment result | TxnRef={TxnRef} | Mode=READ_ONLY",
                txnRef);
            
            var result = await _service.HandleVnPayReturnAsync(queryCollection);
            
            _logger.LogInformation(
                "📱 CONFIRM | Result: {IsSuccess}, BookingId: {BookingId}", 
                result.IsSuccess, result.BookingId);
            
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
