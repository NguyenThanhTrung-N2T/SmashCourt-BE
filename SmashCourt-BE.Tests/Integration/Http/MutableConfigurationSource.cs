using Microsoft.Extensions.Configuration;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Nguồn cấu hình trong bộ nhớ ưu tiên cao nhất của host test, đổi được lúc chạy và báo reload để <c>IOptionsMonitor</c> thấy
/// (BE-IT-19: bật/tắt <c>TadssConfig:EnableTadssDecision</c> không cần khởi động lại).
/// </summary>
public sealed class MutableConfigurationSource : IConfigurationSource
{
    public MutableConfigurationProvider Provider { get; } = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

public sealed class MutableConfigurationProvider : ConfigurationProvider
{
    /// <summary>Đặt giá trị rồi báo reload; giá trị null xóa khóa khỏi nguồn này.</summary>
    public void Update(string key, string? value)
    {
        if (value == null)
            Data.Remove(key);
        else
            Data[key] = value;
        OnReload();
    }
}
