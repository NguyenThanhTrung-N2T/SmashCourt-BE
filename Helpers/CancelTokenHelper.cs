using System.Security.Cryptography;
using System.Text;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Nơi duy nhất sinh, băm token hủy bằng link và tính hạn link, dùng cho mọi đường phát token (IPN, tại quầy, xác nhận không qua VNPay).
    /// Phía tra cứu cũng băm bằng <see cref="Hash"/> để hai phía không lệch.
    /// </summary>
    public static class CancelTokenHelper
    {
        /// <summary>Link hủy sống tối đa chừng này kể từ lúc phát.</summary>
        public const int MaxLifetimeHours = 24;

        private const int TokenBytes = 32;

        // Sinh token mới kèm mã băm và hạn link hủy; playStartUtc là giờ bắt đầu chơi sớm nhất đã đổi sang UTC
        public static CancelTokenData Generate(DateTime playStartUtc, DateTime nowUtc)
        {
            var expiresAtUtc = ComputeExpiry(playStartUtc, nowUtc);

            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            return new CancelTokenData(rawToken, Hash(rawToken), expiresAtUtc);
        }

        // Hạn link hủy = min(giờ bắt đầu chơi, now + 24h); đơn đã tới giờ chơi thì link hết hạn ngay khi phát
        public static DateTime ComputeExpiry(DateTime playStartUtc, DateTime nowUtc)
        {
            EnsureUtc(playStartUtc, nameof(playStartUtc));
            EnsureUtc(nowUtc, nameof(nowUtc));

            var limit = nowUtc.AddHours(MaxLifetimeHours);
            return playStartUtc < limit ? playStartUtc : limit;
        }

        /// <summary>SHA-256 hex chữ thường của token gốc; giá trị lưu ở <c>bookings.cancel_token_hash</c>.</summary>
        public static string Hash(string rawToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(rawToken);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        }

        private static void EnsureUtc(DateTime value, string paramName)
        {
            if (value.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Mốc thời gian phải có Kind = Utc (spec 7.14).", paramName);
        }
    }
}
