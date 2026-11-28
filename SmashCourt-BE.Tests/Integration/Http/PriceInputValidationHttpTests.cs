using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Giá sân, giá dịch vụ có phần lẻ của đồng bị chặn bằng 400 <c>VALIDATION_ERROR</c> qua model binding thật, kể cả phần tử trong danh sách
/// <c>slots</c>; trước khi chặn, giá lẻ làm tổng đơn lẻ mà VNPay chỉ nhận số đồng nguyên.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Pricing")]
public sealed class PriceInputValidationHttpTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : HttpTestBase(database, host)
{
    private async Task<HttpClient> OwnerClientAsync() => await CreateClientForAsync(await SeedUserAsync(UserRole.OWNER));

    private static async Task<(HttpStatusCode Status, JsonElement Envelope)> SendAsync(HttpResponseMessage response)
    {
        using (response)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (response.StatusCode, json.RootElement.Clone());
        }
    }

    private static void AssertRejected((HttpStatusCode Status, JsonElement Envelope) result, string errorKey)
    {
        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Equal(ErrorCodes.ValidationError, result.Envelope.GetProperty("code").GetString());
        var messages = result.Envelope.GetProperty("errors").GetProperty(errorKey).EnumerateArray().Select(m => m.GetString()).ToList();
        Assert.Contains(messages, m => m!.Contains("số đồng nguyên"));
    }

    [Fact]
    public async Task SystemPriceVersion_WithAFractionalSlotPrice_IsRejectedForThatSlot()
    {
        var client = await OwnerClientAsync();
        var effectiveFrom = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

        var result = await SendAsync(await client.PatchAsJsonAsync(
            $"/api/system-prices/versions/{effectiveFrom}?courtTypeId={Guid.NewGuid()}",
            new
            {
                slots = new object[]
                {
                    new { startTime = "06:00:00", endTime = "12:00:00", weekdayPrice = 80_000m, weekendPrice = 90_000m },
                    new { startTime = "12:00:00", endTime = "18:00:00", weekdayPrice = 85_000.5m, weekendPrice = 90_000m }
                }
            }));

        AssertRejected(result, "Slots[1].WeekdayPrice");
        Assert.False(result.Envelope.GetProperty("errors").TryGetProperty("Slots[0].WeekdayPrice", out _));
    }

    [Fact]
    public async Task CreateService_WithAFractionalPrice_IsRejected()
    {
        var client = await OwnerClientAsync();

        var result = await SendAsync(await client.PostAsJsonAsync("/api/services",
            new { name = $"Nước {Guid.NewGuid():N}"[..20], unit = "chai", defaultPrice = 12_000.5m }));

        AssertRejected(result, "DefaultPrice");
    }

    [Fact]
    public async Task CreateService_WithAWholePrice_IsAccepted()
    {
        var client = await OwnerClientAsync();

        var result = await SendAsync(await client.PostAsJsonAsync("/api/services",
            new { name = $"Nước {Guid.NewGuid():N}"[..20], unit = "chai", defaultPrice = 12_000m }));

        Assert.Equal(HttpStatusCode.Created, result.Status);
        Assert.Equal(12_000m, result.Envelope.GetProperty("data").GetProperty("defaultPrice").GetDecimal());
    }
}
