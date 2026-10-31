using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Payments;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;

namespace SmashCourt_BE.Services
{
    /// <summary>
    /// Cổng VNPay: tự dựng URL thanh toán có hạn link (P6-27) và kiểm chữ ký kết quả; cả hai chiều ký bằng <see cref="VnPayHelper"/>.
    /// </summary>
    public class VnPayService : IPaymentGateway
    {
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<VnPayService> _logger;

        private string TmnCode => GetRequiredSetting("VnPay:TmnCode");
        private string HashSecret => GetRequiredSetting("VnPay:HashSecret");
        private string BaseUrl => GetOptionalSetting(
            "VnPay:BaseUrl",
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html");
        private string CallbackUrl => GetRequiredSetting("VnPay:CallbackUrl");
        private string Version => GetOptionalSetting("VnPay:Version", "2.1.0");
        private string OrderType => GetOptionalSetting("VnPay:OrderType", "other");

        public VnPayService(
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor,
            ILogger<VnPayService> logger)
        {
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public PaymentTxMethod Method => PaymentTxMethod.VNPAY;

        // BE tự dựng và ký URL (P6-27): VNPAY.NET không gửi được vnp_ExpireDate và gửi vnp_CreateDate theo UTC (T-5).
        // Tham số đúng bằng bộ thư viện từng gửi, thêm vnp_ExpireDate; hai mốc theo GMT+7 như đặc tả VNPay
        public GatewayPaymentLink CreatePaymentLink(GatewayPaymentRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.CreatedAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Mốc tạo link phải có Kind = Utc.", nameof(request));
            if (request.ExpiresAtUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Hạn link phải có Kind = Utc.", nameof(request));
            if (request.ExpiresAtUtc <= request.CreatedAtUtc)
                throw new ArgumentOutOfRangeException(nameof(request), request.ExpiresAtUtc, "Hạn link phải sau lúc tạo link.");
            if (request.Amount <= 0 || request.Amount != decimal.Truncate(request.Amount))
                throw new ArgumentOutOfRangeException(nameof(request), request.Amount, "Số tiền VNPay phải là số đồng nguyên > 0.");

            // vnp_TxnRef do BE sinh: duy nhất, chỉ chữ và số; IPN tìm Payment theo mã này
            var transactionRef = Guid.NewGuid().ToString("N");
            var parameters = new Dictionary<string, string>
            {
                ["vnp_Version"] = Version,
                ["vnp_Command"] = "pay",
                ["vnp_TmnCode"] = TmnCode,
                ["vnp_Amount"] = ((long)(request.Amount * 100)).ToString(CultureInfo.InvariantCulture),
                ["vnp_CreateDate"] = VnPayHelper.FormatVietnamTime(request.CreatedAtUtc),
                ["vnp_ExpireDate"] = VnPayHelper.FormatVietnamTime(request.ExpiresAtUtc),
                ["vnp_CurrCode"] = "VND",
                ["vnp_IpAddr"] = ResolveClientIp(),
                ["vnp_Locale"] = "vn",
                ["vnp_OrderInfo"] = NormalizeOrderInfo(request.OrderInfo),
                ["vnp_OrderType"] = OrderType,
                ["vnp_ReturnUrl"] = CallbackUrl,
                ["vnp_TxnRef"] = transactionRef
            };
            var url = VnPayHelper.BuildPaymentUrl(BaseUrl, parameters, HashSecret);

            // Không log URL đầy đủ (có chữ ký)
            _logger.LogInformation(
                "VNPay link | RequestedRef: {RequestedRef} | TxnRef: {TxnRef} | Amount: {Amount} | CreateDate: {CreateDate} | ExpireDate: {ExpireDate}",
                request.Reference, transactionRef, request.Amount, parameters["vnp_CreateDate"], parameters["vnp_ExpireDate"]);

            return new GatewayPaymentLink(url, transactionRef);
        }

        public GatewayPaymentResult ReadResult(IQueryCollection query)
        {
            var responseCode = query["vnp_ResponseCode"].ToString();
            // vnp_Amount là số tiền × 100
            decimal? amount = long.TryParse(query["vnp_Amount"], out var rawAmount) ? rawAmount / 100m : null;
            var rawPayload = string.Join("&", query.OrderBy(q => q.Key).Select(q => q.Key + "=" + q.Value));

            return new GatewayPaymentResult(
                IpnProvider.VNPAY,
                query["vnp_TxnRef"].ToString(),
                IsSignatureValid(query, responseCode),
                responseCode == "00",
                amount,
                responseCode,
                rawPayload);
        }

        private bool IsSignatureValid(IQueryCollection query, string responseCode)
        {
            var vnpSecureHash = query["vnp_SecureHash"].ToString();

            // Như GetResponseData() của VnPayLibrary: mọi tham số vnp_ trừ chính chữ ký; ký bằng cùng helper với chiều tạo link
            var signData = VnPayHelper.BuildSignData(query
                .Where(q => q.Key.StartsWith("vnp_", StringComparison.Ordinal)
                            && q.Key != "vnp_SecureHash"
                            && q.Key != "vnp_SecureHashType")
                .Select(q => new KeyValuePair<string, string>(q.Key, q.Value.ToString())));
            var computedHash = VnPayHelper.Sign(signData, HashSecret);
            var match = computedHash.Equals(vnpSecureHash, StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation("VNPay signature — ResponseCode: {C} | HashStr: {H} | Match: {M}",
                responseCode, signData, match);

            return match;
        }

        private string GetRequiredSetting(string key)
        {
            var value = _config[key]?.Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"{key} is not configured");

            return value;
        }

        private string GetOptionalSetting(string key, string defaultValue)
        {
            var value = _config[key]?.Trim();
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        }

        private string ResolveClientIp()
        {
            var forwardedFor = _httpContextAccessor.HttpContext?.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var forwardedIp = forwardedFor
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(forwardedIp))
                    return NormalizeIpAddress(forwardedIp);
            }

            var remoteIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
            if (remoteIp is null)
            {
                _logger.LogWarning("VNPay request IP missing from HttpContext. Falling back to localhost.");
                return "127.0.0.1";
            }

            if (remoteIp.IsIPv4MappedToIPv6)
                remoteIp = remoteIp.MapToIPv4();

            if (IPAddress.IPv6Loopback.Equals(remoteIp))
                return "127.0.0.1";

            return NormalizeIpAddress(remoteIp.ToString());
        }

        private static string NormalizeOrderInfo(string orderInfo)
        {
            if (string.IsNullOrWhiteSpace(orderInfo))
                return "Thanh toan don hang";

            orderInfo = orderInfo.Replace('\u0110', 'D').Replace('\u0111', 'd');

            var normalized = orderInfo.Normalize(NormalizationForm.FormD);
            var stripped = new StringBuilder();

            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    stripped.Append(c);
            }

            var ascii = stripped
                .ToString()
                .Normalize(NormalizationForm.FormC);

            ascii = Regex.Replace(ascii, "[^A-Za-z0-9 .,]", " ");
            ascii = Regex.Replace(ascii, "\\s+", " ").Trim();

            if (string.IsNullOrWhiteSpace(ascii))
                return "Thanh toan don hang";

            return ascii.Length <= 255
                ? ascii
                : ascii[..255].Trim();
        }

        private string NormalizeIpAddress(string ipAddress)
        {
            if (!IPAddress.TryParse(ipAddress, out var parsedIp))
            {
                _logger.LogWarning("VNPay received invalid IP address format: {Ip}. Falling back to localhost.", ipAddress);
                return "127.0.0.1";
            }

            if (parsedIp.IsIPv4MappedToIPv6)
                parsedIp = parsedIp.MapToIPv4();

            if (IPAddress.IsLoopback(parsedIp))
                return "127.0.0.1";

            // Sandbox test qua Docker thường chỉ lấy được private bridge IP, VNPAY đôi lúc reject format này.
            var bytes = parsedIp.GetAddressBytes();
            var isPrivateIpv4 =
                parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                (
                    bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168)
                );

            if (isPrivateIpv4)
            {
                _logger.LogInformation("VNPay IP {Ip} is private/local. Using 127.0.0.1 for sandbox compatibility.", ipAddress);
                return "127.0.0.1";
            }

            return parsedIp.ToString();
        }
    }
}
