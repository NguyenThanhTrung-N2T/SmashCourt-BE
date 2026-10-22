using System.Security.Cryptography;
using System.Text;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Token hủy bằng link (spec Phase 6, 7.9, D8): nơi duy nhất sinh token, băm token và tính hạn link hủy, dùng chung cho
    /// đơn online thanh toán qua VNPay (IPN), đơn tại quầy và đơn online xác nhận không qua VNPay (a′, duyệt 0 %).
    /// Phía tra cứu (<c>GetCancelInfoAsync</c>, <c>CancelByTokenAsync</c>) cũng băm bằng <see cref="Hash"/> để hai phía không lệch.
    /// </summary>
    public static class CancelTokenHelper
    {
        /// <summary>Link hủy sống tối đa chừng này kể từ lúc phát.</summary>
        public const int MaxLifetimeHours = 24;

        private const int TokenBytes = 32;

        /// <summary>Sinh token mới kèm mã băm và hạn link hủy.</summary>
        /// <param name="playStartUtc">Giờ bắt đầu chơi sớm nhất của đơn (đổi từ giờ VN bằng <c>DateTimeHelper.ToUtcFromVietnam</c>), Kind = Utc</param>
        /// <param name="nowUtc">Thời điểm phát token, Kind = Utc</param>
        public static CancelTokenData Generate(DateTime playStartUtc, DateTime nowUtc)
        {
            var expiresAtUtc = ComputeExpiry(playStartUtc, nowUtc);

            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
            return new CancelTokenData(rawToken, Hash(rawToken), expiresAtUtc);
        }

        /// <summary>
        /// Hạn link hủy = <c>min(giờ bắt đầu chơi, now + 24h)</c> (spec 7.9; sửa T-2 lệch 7 giờ và T-3 trộn Kind).
        /// Đơn đã tới giờ chơi thì hạn bằng giờ chơi, tức link đã hết hạn ngay khi phát.
        /// </summary>
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
