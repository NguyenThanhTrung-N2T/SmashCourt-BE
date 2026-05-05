namespace SmashCourt_BE.Helpers
{
    public static class DateTimeHelper
    {
        public static readonly TimeZoneInfo VNTimezone = GetVietnamTimeZone();

        private static TimeZoneInfo GetVietnamTimeZone()
        {
            // Windows: "SE Asia Standard Time"
            // Linux/macOS: "Asia/Ho_Chi_Minh"
            return OperatingSystem.IsWindows()
                ? TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
                : TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }

        /// <summary>
        /// Lấy ngày hiện tại theo giờ Việt Nam
        /// </summary>
        public static DateOnly GetTodayInVietnam()
        {
            var vnNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, VNTimezone);
            return DateOnly.FromDateTime(vnNow);
        }

        /// <summary>
        /// Lấy thời gian hiện tại theo giờ Việt Nam
        /// </summary>
        public static DateTime GetNowInVietnam()
        {
            return TimeZoneInfo.ConvertTime(DateTime.UtcNow, VNTimezone);
        }
    }
}
