namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Helper để map tier name sang color và icon
    /// TODO: Sau này nên thêm Color và Icon vào database (LoyaltyTier table)
    /// </summary>
    public static class LoyaltyTierHelper
    {
        private static readonly Dictionary<string, (string Color, string Icon)> TierStyles = new()
        {
            { "Bronze", ("#CD7F32", "🥉") },
            { "Silver", ("#C0C0C0", "🥈") },
            { "Gold", ("#FFD700", "🥇") },
            { "Platinum", ("#E5E4E2", "💎") },
            { "Diamond", ("#B9F2FF", "💠") }
        };

        /// <summary>
        /// Lấy màu sắc của tier (hex color code)
        /// </summary>
        public static string GetTierColor(string tierName)
        {
            return TierStyles.TryGetValue(tierName, out var style) 
                ? style.Color 
                : "#808080"; // Default gray
        }

        /// <summary>
        /// Lấy icon của tier (emoji)
        /// </summary>
        public static string GetTierIcon(string tierName)
        {
            return TierStyles.TryGetValue(tierName, out var style) 
                ? style.Icon 
                : "⭐"; // Default star
        }
    }
}
