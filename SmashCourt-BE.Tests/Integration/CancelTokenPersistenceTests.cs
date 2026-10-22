using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// T-3 trên schema thật (BE-IT-25, phần lưu token): hạn link hủy là cột timestamptz nên chỉ nhận Kind = Utc. Công thức cũ của đơn
/// tại quầy đưa giờ VN Kind = Unspecified vào đây (Npgsql từ chối, catch nuốt ⇒ không có email); hạn từ CancelTokenHelper ghi được
/// và đọc lại đúng thời điểm. Phần gửi email qua HTTP thuộc bộ BE-IT của 6A-2a/2c.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CancelTokenPersistenceTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    [Fact]
    [Trait("Catalog", "BE-IT-25")]
    public async Task UpdateAsync_WithOldCounterFormulaUnspecifiedExpiry_IsRejectedByNpgsql()
    {
        // Đặc tả lại gốc lỗi T-3 (không phải hành vi mong muốn): giá trị cũ min(giờ VN Unspecified, UtcNow + 24h) khi đơn trong ngày
        var order = await SeedOrderAsync();
        order.Booking.CancelTokenHash = CancelTokenHelper.Hash("raw");
        order.Booking.CancelTokenExpiresAt = PlayDate.ToDateTime(Start); // Kind = Unspecified

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Repository.UpdateAsync(order.Booking));
        Assert.Contains("Kind=Unspecified", ex.ToString());
    }

    [Fact]
    [Trait("Catalog", "BE-IT-25")]
    public async Task UpdateAsync_WithHelperExpiry_PersistsExactUtcInstant()
    {
        var order = await SeedOrderAsync();
        var playStartUtc = DateTimeHelper.ToUtcFromVietnam(PlayDate, Start);
        var token = CancelTokenHelper.Generate(playStartUtc, playStartUtc.AddHours(-3));
        order.Booking.CancelTokenHash = token.TokenHash;
        order.Booking.CancelTokenExpiresAt = token.ExpiresAtUtc;

        await Repository.UpdateAsync(order.Booking);

        var reloaded = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(playStartUtc, reloaded.CancelTokenExpiresAt);
        Assert.Equal(DateTimeKind.Utc, reloaded.CancelTokenExpiresAt!.Value.Kind);
        // 18:00 giờ VN = 11:00 UTC: không còn lệch 7 giờ
        Assert.Equal(new DateTime(2026, 11, 21, 11, 0, 0, DateTimeKind.Utc), reloaded.CancelTokenExpiresAt);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-25")]
    public async Task TokenFromHelper_RoundTripsThroughLookupAndSingleUseConsumption()
    {
        var order = await SeedOrderAsync(BookingStatus.CONFIRMED);
        var token = CancelTokenHelper.Generate(DateTimeHelper.ToUtcFromVietnam(PlayDate, Start), Now);
        order.Booking.CancelTokenHash = token.TokenHash;
        order.Booking.CancelTokenExpiresAt = token.ExpiresAtUtc;
        await Repository.UpdateAsync(order.Booking);

        var found = await Repository.GetByCancelTokenAsync(CancelTokenHelper.Hash(token.RawToken));

        Assert.NotNull(found);
        Assert.Equal(order.Booking.Id, found.Id);
        Assert.Null(await Repository.GetByCancelTokenAsync(CancelTokenHelper.Hash(token.RawToken + "x")));
        Assert.True(await Repository.TryConsumeTokenAsync(order.Booking.Id, token.TokenHash, Now));
        Assert.False(await Repository.TryConsumeTokenAsync(order.Booking.Id, token.TokenHash, Now));
    }
}
