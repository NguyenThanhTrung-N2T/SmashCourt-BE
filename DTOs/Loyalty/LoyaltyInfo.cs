namespace SmashCourt_BE.DTOs.Loyalty
{
    /// <summary>
    /// Thông tin hạng thành viên của khách hàng
    /// Dùng để hiển thị badge, progress bar, và CTA trong UI
    /// </summary>
    public class LoyaltyInfo
    {
        /// <summary>
        /// Tên hạng thành viên (Bronze, Silver, Gold, Platinum, Diamond)
        /// </summary>
        public string TierName { get; set; } = null!;

        /// <summary>
        /// Màu sắc của hạng (hex color code) - để FE render badge đẹp
        /// </summary>
        public string TierColor { get; set; } = null!;

        /// <summary>
        /// Icon của hạng (emoji hoặc icon name) - để FE render badge
        /// </summary>
        public string TierIcon { get; set; } = null!;

        /// <summary>
        /// Tỷ lệ giảm giá của hạng hiện tại (%)
        /// </summary>
        public decimal DiscountRate { get; set; }

        /// <summary>
        /// Điểm tích lũy hiện tại của khách hàng
        /// </summary>
        public int CurrentPoints { get; set; }

        /// <summary>
        /// Điểm cần đạt để lên hạng tiếp theo
        /// Nếu đã ở hạng cao nhất → bằng CurrentPoints
        /// </summary>
        public int NextTierPoints { get; set; }

        /// <summary>
        /// Tên hạng tiếp theo (null nếu đã ở hạng cao nhất)
        /// </summary>
        public string? NextTierName { get; set; }

        /// <summary>
        /// Phần trăm tiến độ để lên hạng tiếp theo (0-100)
        /// Dùng để render progress bar
        /// </summary>
        public double ProgressPercentage { get; set; }

        /// <summary>
        /// Số điểm còn thiếu để lên hạng tiếp theo
        /// Dùng để hiển thị CTA: "Chỉ còn 800 điểm để lên GOLD!"
        /// </summary>
        public int PointsToNextTier { get; set; }
    }
}
