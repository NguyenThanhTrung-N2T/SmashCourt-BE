namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Luật dữ liệu Quản lý nhập khi duyệt / từ chối mức cọc, khớp luật của TADSS để BE chặn trước
    /// (mỗi quyết định chỉ được ghi đè một lần).
    /// </summary>
    public static class DepositApprovalRules
    {
        public const int MaxRateDecimals = 4;
        public const int MinReasonLength = 5;
        public const int MaxReasonLength = 500;

        public const string InvalidRateMessage = "Tỷ lệ cọc phải từ 0 đến 1 và có tối đa 4 chữ số thập phân";
        public const string InvalidReasonMessage = "Lý do phải từ 5 đến 500 ký tự và không được chỉ gồm khoảng trắng";

        /// <summary>
        /// Tỷ lệ trong [0, 1], tối đa 4 chữ số thập phân. So bằng làm tròn nên số 0 thừa (<c>0.30000</c>) vẫn hợp lệ,
        /// còn <c>0.12345</c> bị từ chối thay vì làm tròn âm thầm.
        /// </summary>
        public static bool IsValidRate(decimal? rate) =>
            rate is >= 0m and <= 1m && decimal.Round(rate.Value, MaxRateDecimals) == rate.Value;

        /// <summary>
        /// Lý do 5..500 Unicode code point trên chuỗi gốc (TADSS đếm bằng <c>EnumerateRunes</c>, emoji tính một),
        /// không chỉ gồm khoảng trắng.
        /// </summary>
        public static bool IsValidReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            var length = StringHelper.CountCodePoints(reason);
            return length is >= MinReasonLength and <= MaxReasonLength;
        }
    }
}
