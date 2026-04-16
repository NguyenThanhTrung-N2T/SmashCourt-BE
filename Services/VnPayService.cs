using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services.IService;
using System.Security.Cryptography;
using System.Text;

namespace SmashCourt_BE.Services
{
    public class VnPayService : IVnPayService
    {
        private readonly IConfiguration _config;

        private string TmnCode => _config["VnPay:TmnCode"]!;
        private string HashSecret => _config["VnPay:HashSecret"]!;
        private string BaseUrl => _config["VnPay:BaseUrl"]
            ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
        private string ReturnUrl => _config["VnPay:ReturnUrl"]!;

        public VnPayService(IConfiguration config)
        {
            _config = config;
        }

        public string CreatePaymentUrl(
            string transactionRef, decimal amount, string orderInfo)
        {
            var vnpParams = new SortedDictionary<string, string>
            {
                ["vnp_Version"] = "2.1.0",
                ["vnp_Command"] = "pay",
                ["vnp_TmnCode"] = TmnCode,
                ["vnp_Amount"] = ((long)(amount * 100)).ToString(),
                ["vnp_CreateDate"] = DateTimeHelper.GetNowInVietnam()
                    .ToString("yyyyMMddHHmmss"),
                ["vnp_CurrCode"] = "VND",
                ["vnp_IpAddr"] = "127.0.0.1",
                ["vnp_Locale"] = "vn",
                ["vnp_OrderInfo"] = orderInfo,
                ["vnp_OrderType"] = "other",
                ["vnp_ReturnUrl"] = ReturnUrl,
                ["vnp_TxnRef"] = transactionRef,
                ["vnp_ExpireDate"] = DateTimeHelper.GetNowInVietnam().AddMinutes(15)
                    .ToString("yyyyMMddHHmmss")
            };

            var queryString = string.Join("&",
                vnpParams.Select(p =>
                    $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

            var secureHash = ComputeHmacSha512(HashSecret, queryString);

            return $"{BaseUrl}?{queryString}&vnp_SecureHash={secureHash}";
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

            // Build raw payload để log
            rawPayload = string.Join("&",
                query.OrderBy(q => q.Key)
                    .Select(q => $"{q.Key}={q.Value}"));

            // Lấy secure hash từ VNPay
            var vnpSecureHash = query["vnp_SecureHash"].ToString();

            // Build chuỗi để verify — bỏ vnp_SecureHash
            var verifyParams = new SortedDictionary<string, string>();
            foreach (var (key, value) in query)
            {
                if (key.StartsWith("vnp_") && key != "vnp_SecureHash")
                    verifyParams[key] = value.ToString();
            }

            var queryString = string.Join("&",
                verifyParams.Select(p =>
                    $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

            var computedHash = ComputeHmacSha512(HashSecret, queryString);

            return computedHash.Equals(vnpSecureHash,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeHmacSha512(string key, string data)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            using var hmac = new HMACSHA512(keyBytes);
            var hash = hmac.ComputeHash(dataBytes);
            return Convert.ToHexString(hash).ToLower();
        }
    }
}
