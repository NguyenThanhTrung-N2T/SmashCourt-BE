using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// BE-UT-07 (phần JSON), gói 6A-2c Task 2: serialize bằng đúng <c>JsonOptions</c> của MVC trong host thật (camelCase, bộ chuyển giờ VN),
/// để chắc khóa có giá trị null vẫn có mặt và tên khóa khớp DEPOSIT_APPROVAL_API 2.2 / 3.1.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Catalog", "BE-UT-07")]
public sealed class HttpDepositDtoJsonTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    private async Task<JsonElement> SerializeAsync<T>(T value)
    {
        var factory = await GetFactoryAsync();
        var options = factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var json = JsonSerializer.Serialize(ApiResponse<T>.Ok(value, "ok"), options);
        return JsonDocument.Parse(json).RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task PendingApprovalResponse_SerializesNullDepositKeys_AndTheApprovalDeadlineInVietnamTime()
    {
        var data = await SerializeAsync(new OnlineBookingResponse
        {
            BookingId = Guid.NewGuid(),
            BookingCode = "BK-20261118-000123",
            Status = "PENDING_APPROVAL",
            FinalTotal = 228_000m,
            ApprovalExpiresAt = new DateTime(2026, 11, 18, 2, 45, 0, DateTimeKind.Utc)
        });

        foreach (var key in new[] { "paymentUrl", "expiresAt", "paymentLinkExpiresAt", "depositRate", "depositAmount", "remainingAmount", "depositSource" })
            Assert.Equal(JsonValueKind.Null, data.GetProperty(key).ValueKind);
        Assert.Equal("BK-20261118-000123", data.GetProperty("bookingCode").GetString());
        Assert.Equal("PENDING_APPROVAL", data.GetProperty("status").GetString());
        Assert.Equal("18/11/2026 09:45:00", data.GetProperty("approvalExpiresAt").GetString());
    }

    [Fact]
    public async Task OnlineResponse_HasExactlyTheDocumentedKeys()
    {
        var data = await SerializeAsync(new OnlineBookingResponse { BookingCode = "BK", Status = "PENDING" });

        Assert.Equal(
            new[]
            {
                "bookingId", "bookingCode", "status", "paymentUrl", "expiresAt", "paymentLinkExpiresAt", "finalTotal", "depositRate", "depositAmount",
                "remainingAmount", "depositSource", "approvalExpiresAt"
            },
            data.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task BookingDto_SerializesDepositApprovalAndCancelKeys_EvenWhenNull()
    {
        var data = await SerializeAsync(new BookingDto { BookingCode = "BK", BranchName = "CN", Status = "CONFIRMED", Source = "WALK_IN", PaymentStatus = "UNPAID" });

        foreach (var key in new[]
                 {
                     "depositRate", "depositAmount", "remainingAmount", "depositSource", "approvalExpiresAt", "approvalReasonCode",
                     "approvalReasons", "cancelReason"
                 })
            Assert.Equal(JsonValueKind.Null, data.GetProperty(key).ValueKind);
    }

    [Fact]
    public async Task BookingDto_SerializesValuesWithDocumentedNamesAndTypes()
    {
        var data = await SerializeAsync(new BookingDto
        {
            BookingCode = "BK", BranchName = "CN", Status = "PAID_ONLINE", Source = "ONLINE", PaymentStatus = "PARTIALLY_PAID",
            DepositRate = 0.3m, DepositAmount = 68_400m, RemainingAmount = 159_600m, DepositSource = "MANAGER",
            ApprovalReasonCode = "NO_SHOW_HISTORY_REVIEW", ApprovalReasons = ["Khách có 3 lần không đến (ngưỡng 2)."],
            CancelReason = null
        });

        Assert.Equal(0.3m, data.GetProperty("depositRate").GetDecimal());
        Assert.Equal(68_400m, data.GetProperty("depositAmount").GetDecimal());
        Assert.Equal(159_600m, data.GetProperty("remainingAmount").GetDecimal());
        Assert.Equal("MANAGER", data.GetProperty("depositSource").GetString());
        Assert.Equal(JsonValueKind.Array, data.GetProperty("approvalReasons").ValueKind);
        Assert.Equal("Khách có 3 lần không đến (ngưỡng 2).", data.GetProperty("approvalReasons")[0].GetString());
    }
}
