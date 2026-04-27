using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Helpers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;

namespace SmashCourt_BE.Services
{
    // ===== Copy CHÍNH XÁC từ VnPayLibrary.cs chính thức của VNPay =====

    /// <summary>
    /// Comparer giống hệt VnPayCompare trong demo chính thức — ordinal en-US
    /// </summary>
    internal class VnPayCompare : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            var vnpCompare = CompareInfo.GetCompareInfo("en-US");
            return vnpCompare.Compare(x, y, CompareOptions.Ordinal);
        }
    }

    public class VnPayService : IVnPayService
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

        public string CreatePaymentUrl(string transactionRef, decimal amount, string orderInfo)
        {
            transactionRef = NormalizeTransactionRef(transactionRef);
            orderInfo = NormalizeOrderInfo(orderInfo);
            var clientIp = ResolveClientIp();

            _logger.LogInformation(
                "VNPay config loaded | TmnCode: {T} | HashSecret length: {L} | CallbackUrl: {U} | ClientIp: {Ip}",
                TmnCode,
                HashSecret.Length,
                CallbackUrl,
                clientIp);

            var now = DateTimeHelper.GetNowInVietnam();

            // === Dùng SortedList + VnPayCompare GIỐNG HỆT VnPayLibrary.cs chính thức ===
            var requestData = new SortedList<string, string>(new VnPayCompare());

            void Add(string key, string value)
            {
                if (!string.IsNullOrEmpty(value)) requestData[key] = value;
            }

            Add("vnp_Version",    Version);
            Add("vnp_Command",    "pay");
            Add("vnp_TmnCode",    TmnCode);
            Add("vnp_Amount",     ((long)(amount * 100)).ToString());
            Add("vnp_CreateDate", now.ToString("yyyyMMddHHmmss"));
            Add("vnp_CurrCode",   "VND");
            Add("vnp_ExpireDate", now.AddMinutes(15).ToString("yyyyMMddHHmmss"));
            Add("vnp_IpAddr",     clientIp);
            Add("vnp_Locale",     "vn");
            Add("vnp_OrderInfo",  orderInfo);
            Add("vnp_OrderType",  OrderType);
            Add("vnp_ReturnUrl",  CallbackUrl);
            Add("vnp_TxnRef",     transactionRef);

            // === CreateRequestUrl — copy y chang VnPayLibrary.cs ===
            var data = new StringBuilder();
            foreach (var kv in requestData)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    data.Append(WebUtility.UrlEncode(kv.Key)
                        + "=" + WebUtility.UrlEncode(kv.Value) + "&");
                }
            }

            string queryString = data.ToString();
            string signData    = queryString;
            if (signData.Length > 0)
                signData = signData.Remove(data.Length - 1, 1); // bỏ trailing &

            string secureHash = HmacSha512(HashSecret, signData);

            string fullUrl = BaseUrl + "?" + queryString + "vnp_SecureHash=" + secureHash;

            _logger.LogInformation("SignData  : {S}", signData);
            _logger.LogInformation("SecureHash: {H}", secureHash);
            _logger.LogInformation("FullURL   : {U}", fullUrl);

            return fullUrl;
        }

        public bool VerifyIpn(
            IQueryCollection query,
            out string transactionRef,
            out bool isSuccess,
            out string rawPayload)
        {
            transactionRef = query["vnp_TxnRef"].ToString();
            var responseCode = query["vnp_ResponseCode"].ToString();
            isSuccess = responseCode == "00";
            rawPayload = string.Join("&", query.OrderBy(q => q.Key)
                .Select(q => q.Key + "=" + q.Value));

            var vnpSecureHash = query["vnp_SecureHash"].ToString();

            // === ValidateSignature — copy y chang GetResponseData() trong VnPayLibrary.cs ===
            var responseData = new SortedList<string, string>(new VnPayCompare());
            foreach (var (key, value) in query)
            {
                if (key.StartsWith("vnp_")
                    && key != "vnp_SecureHash"
                    && key != "vnp_SecureHashType"
                    && !string.IsNullOrEmpty(value))
                    responseData[key] = value.ToString();
            }

            var sb = new StringBuilder();
            foreach (var kv in responseData)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                    sb.Append(WebUtility.UrlEncode(kv.Key)
                        + "=" + WebUtility.UrlEncode(kv.Value) + "&");
            }
            if (sb.Length > 0) sb.Remove(sb.Length - 1, 1);

            string hashStr     = sb.ToString();
            string computedHash = HmacSha512(HashSecret, hashStr);
            bool match = computedHash.Equals(vnpSecureHash, StringComparison.OrdinalIgnoreCase);

            _logger.LogInformation("VerifyIpn — ResponseCode: {C} | HashStr: {H} | Match: {M}",
                responseCode, hashStr, match);

            return match;
        }

        // Giống hệt Utils.HmacSHA512 trong VnPayLibrary.cs
        private static string HmacSha512(string key, string data)
        {
            var hash = new StringBuilder();
            byte[] keyBytes  = Encoding.UTF8.GetBytes(key);
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            using var hmac = new HMACSHA512(keyBytes);
            byte[] hashValue = hmac.ComputeHash(dataBytes);
            foreach (var b in hashValue)
                hash.Append(b.ToString("x2"));
            return hash.ToString();
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
                    return forwardedIp;
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

            return remoteIp.ToString();
        }

        private static string NormalizeTransactionRef(string transactionRef)
        {
            if (string.IsNullOrWhiteSpace(transactionRef))
                throw new InvalidOperationException("VNPay transaction reference is required");

            var normalized = Regex.Replace(transactionRef.Trim(), "[^A-Za-z0-9]", string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException("VNPay transaction reference is invalid after normalization");

            return normalized.Length <= 100
                ? normalized
                : normalized[..100];
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
    }
}
