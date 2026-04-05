namespace SmashCourt_BE.Helpers
{
    public static class DateTimeHelper
    {
        private static readonly TimeZoneInfo VNTimezone =
            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

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
