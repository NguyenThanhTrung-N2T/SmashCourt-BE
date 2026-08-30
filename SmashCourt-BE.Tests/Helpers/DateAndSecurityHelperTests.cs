using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Helpers;

public class DateAndSecurityHelperTests
{
    [Theory]
    [InlineData("09:30", 9, 30)]
    [InlineData("09:30:15", 9, 30)]
    public void TryParseTimeOnly_AcceptsSupportedFormats(string value, int hour, int minute)
    {
        Assert.True(DateTimeHelper.TryParseTimeOnly(value, out var result));
        Assert.Equal(new TimeOnly(hour, minute, value.Length > 5 ? 15 : 0), result);
    }

    [Theory]
    [InlineData("9:30")]
    [InlineData("invalid")]
    public void TryParseTimeOnly_RejectsUnsupportedFormats(string value)
    {
        Assert.False(DateTimeHelper.TryParseTimeOnly(value, out _));
    }

    [Fact]
    public void ToUtcFromVietnam_AndBack_PreservesLocalTime()
    {
        var localDate = new DateOnly(2026, 6, 15);
        var localTime = new TimeOnly(18, 30);

        var utc = DateTimeHelper.ToUtcFromVietnam(localDate, localTime);
        var vietnam = DateTimeHelper.ToVietnamTime(utc);

        Assert.Equal(localDate, DateOnly.FromDateTime(vietnam));
        Assert.Equal(localTime, TimeOnly.FromDateTime(vietnam));
    }

    [Fact]
    public void VietnamDateTimeConverter_WritesVietnameseDateFormat()
    {
        var utc = new DateTime(2026, 6, 15, 11, 30, 0, DateTimeKind.Utc);

        var json = JsonSerializer.Serialize(utc, new JsonSerializerOptions
        {
            Converters = { new VietnamDateTimeConverter() }
        });

        Assert.Equal("\"15/06/2026 18:30:00\"", json);
    }

    [Fact]
    public void OtpService_HashesAndVerifiesCode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Otp:HmacSecret"] = "test-secret" })
            .Build();
        var service = new OtpService(configuration);

        var hash = service.HashCode("123456");

        Assert.True(service.VerifyCode("123456", hash));
        Assert.False(service.VerifyCode("654321", hash));
    }
}
