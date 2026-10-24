using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Tùy chọn JSON riêng cho contract TADSS v2: camelCase, enum dạng chuỗi (không nhận số), bỏ trường null khi ghi.
/// Cố ý không dùng <c>VietnamDateTimeConverter</c> của MVC: mọi thời gian của DTO là <see cref="DateTimeOffset"/> kèm offset.
/// </summary>
public static class TadssJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }
}
