using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Nơi duy nhất ký theo cách của VNPay, dùng cho cả chiều tạo link thanh toán lẫn chiều kiểm chữ ký kết quả (IPN, trình duyệt quay về).
    /// Cùng tập tham số thì chuỗi ký, chữ ký và URL trùng từng ký tự với thư viện VNPAY.NET 2.1.0 (test đối chứng ở project test).
    /// </summary>
    public static class VnPayHelper
    {
        private const string TimeFormat = "yyyyMMddHHmmss";

        // Chuỗi ký: bỏ giá trị rỗng, sắp khóa theo ordinal, nối key=UrlEncode(value) bằng '&' (dấu cách thành '+', mã hex chữ hoa)
        public static string BuildSignData(IEnumerable<KeyValuePair<string, string>> parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            return string.Join("&", parameters
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => p.Key + "=" + WebUtility.UrlEncode(p.Value)));
        }

        // HMAC-SHA512 của chuỗi ký bằng HashSecret, hex chữ hoa như thư viện; VNPay so chữ ký không phân biệt hoa thường
        public static string Sign(string signData, string hashSecret)
        {
            ArgumentNullException.ThrowIfNull(signData);
            ArgumentException.ThrowIfNullOrEmpty(hashSecret);

            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hashSecret));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signData)));
        }

        // baseUrl?{chuỗi ký}&vnp_SecureHash={chữ ký}
        public static string BuildPaymentUrl(string baseUrl, IReadOnlyDictionary<string, string> parameters, string hashSecret)
        {
            ArgumentException.ThrowIfNullOrEmpty(baseUrl);

            var signData = BuildSignData(parameters);
            return $"{baseUrl}?{signData}&vnp_SecureHash={Sign(signData, hashSecret)}";
        }

        // Mốc UTC → "yyyyMMddHHmmss" theo giờ Việt Nam (GMT+7) như đặc tả VNPay; phần lẻ giây bị cắt
        public static string FormatVietnamTime(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Mốc thời gian gửi VNPay phải có Kind = Utc.", nameof(utc));

            return DateTimeHelper.ToVietnamTime(utc).ToString(TimeFormat, CultureInfo.InvariantCulture);
        }
    }
}
