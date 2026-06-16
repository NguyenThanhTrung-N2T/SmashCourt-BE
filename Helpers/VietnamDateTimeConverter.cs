using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Custom JSON converter: tự động convert DateTime UTC → giờ Việt Nam khi serialize ra JSON.
    /// Format output: "dd/MM/yyyy HH:mm:ss" (giờ VN, không có timezone offset).
    /// Áp dụng toàn cục cho tất cả API response.
    /// </summary>
    public class VietnamDateTimeConverter : JsonConverter<DateTime>
    {
        private const string OutputFormat = "dd/MM/yyyy HH:mm:ss";

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Parse string → DateTime (dùng khi FE gửi string datetime lên)
            var raw = reader.GetString();
            if (DateTime.TryParse(raw, out var dt))
                return dt;
            throw new JsonException($"Cannot parse '{raw}' as DateTime");
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            // Npgsql EnableLegacyTimestampBehavior=true đọc timestamptz ra:
            //   Kind=Local  → đã convert sang giờ server local (VN nếu máy set VN timezone)
            //   Kind=Utc    → giờ UTC thuần
            //   Kind=Unspecified → coi như UTC (trường hợp timestamp không có tz)
            var utcValue = value.Kind switch
            {
                DateTimeKind.Utc   => value,
                DateTimeKind.Local => value.ToUniversalTime(),    // Local→UTC dùng timezone máy chủ
                _                  => DateTime.SpecifyKind(value, DateTimeKind.Utc)  // Unspecified→UTC
            };

            var vnTime = TimeZoneInfo.ConvertTimeFromUtc(utcValue, DateTimeHelper.VNTimezone);
            writer.WriteStringValue(vnTime.ToString(OutputFormat));
        }
    }

    /// <summary>
    /// Nullable DateTime converter — cùng logic, trả null nếu value null.
    /// </summary>
    public class NullableVietnamDateTimeConverter : JsonConverter<DateTime?>
    {
        private static readonly VietnamDateTimeConverter _inner = new();

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return _inner.Read(ref reader, typeof(DateTime), options);
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value == null) { writer.WriteNullValue(); return; }
            _inner.Write(writer, value.Value, options);
        }
    }
}
