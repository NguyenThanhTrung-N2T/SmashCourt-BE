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
        /// Lấy thời gian UTC hiện tại (DateTime với Kind=Utc)
        /// Dùng để SO SÁNH với timestamp từ database (database lưu UTC, EF Core đọc ra UTC)
        /// </summary>
        public static DateTime GetUtcNow()
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

        /// <summary>
        /// Lấy thời gian hiện tại theo giờ Việt Nam (DateTime với Kind=Unspecified, giờ VN)
        /// Dùng để so sánh với ngày tháng VN (không phải UTC)
        /// </summary>
        public static DateTime GetVietnamNow()
        {
            return TimeZoneInfo.ConvertTime(DateTime.UtcNow, VNTimezone);
        }

        /// <summary>
        /// Convert ngày + giờ Việt Nam sang UTC.
        /// Dùng khi lưu timestamp từ dữ liệu VN vào DB (tránh lỗi SpecifyKind Utc gây lệch 7 tiếng).
        /// </summary>
        public static DateTime ToUtcFromVietnam(DateOnly date, TimeOnly time)
        {
            var vnDateTime = date.ToDateTime(time);
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(vnDateTime, DateTimeKind.Unspecified),
                VNTimezone);
        }

        /// <summary>
        /// Thử parse chuỗi thời gian hỗ trợ cả định dạng HH:mm:ss và HH:mm
        /// </summary>
        public static bool TryParseTimeOnly(string value, out TimeOnly time)
        {
            string[] formats = ["HH:mm:ss", "HH:mm"];
            return TimeOnly.TryParseExact(value, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);
        }
    }
}
