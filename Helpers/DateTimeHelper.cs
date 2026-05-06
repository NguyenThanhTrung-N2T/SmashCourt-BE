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
        /// Lấy thời gian hiện tại theo giờ Việt Nam (DateTime với Kind=Utc)
        /// Dùng để SO SÁNH với timestamp từ database (database lưu UTC, EF Core đọc ra UTC)
        /// </summary>
        public static DateTime GetNowInVietnam()
        {
            // Trả về UTC time - vì database lưu UTC và EF Core đọc ra UTC (EnableLegacyTimestampBehavior=false)
            // Khi so sánh, cả 2 bên đều là UTC nên kết quả chính xác
            return DateTime.UtcNow;
        }
        
        /// <summary>
        /// Convert UTC DateTime sang giờ Việt Nam để hiển thị
        /// </summary>
        public static DateTime ToVietnamTime(DateTime utcTime)
        {
            if (utcTime.Kind != DateTimeKind.Utc)
                utcTime = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
            
            return TimeZoneInfo.ConvertTime(utcTime, VNTimezone);
        }
    }
}
