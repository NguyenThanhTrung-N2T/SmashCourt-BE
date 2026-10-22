using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Tests.Helpers;

/// <summary>
/// CancelTokenHelper (D8, sửa T-2/T-3; spec 7.9): một nơi sinh token, băm token và tính hạn link hủy cho mọi đường phát token.
/// Hạn = min(giờ bắt đầu chơi, now + 24h), cả hai mốc là thời điểm UTC thật; mọi giá trị mong đợi tính tay từ giờ Việt Nam.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Token)]
public class CancelTokenHelperTests
{
    // 20/11/2026 10:00 giờ VN
    private static readonly DateTime Now = new(2026, 11, 20, 3, 0, 0, DateTimeKind.Utc);

    private static DateTime Vn(int year, int month, int day, int hour, int minute) =>
        DateTimeHelper.ToUtcFromVietnam(new DateOnly(year, month, day), new TimeOnly(hour, minute));

    // ───────────────────────── Hạn link hủy ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ComputeExpiry_WhenPlayIsLaterToday_ExpiresAtPlayStartInUtc()
    {
        // Đặt 10:00 VN cho 19:00 VN cùng ngày: hạn 19:00 VN = 12:00 UTC
        var expiry = CancelTokenHelper.ComputeExpiry(Vn(2026, 11, 20, 19, 0), Now);

        Assert.Equal(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc), expiry);
        Assert.Equal(DateTimeKind.Utc, expiry.Kind);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ComputeExpiry_WhenPlayIsLaterToday_IsNotTheOldSevenHoursLateValue()
    {
        // T-2: công thức cũ SpecifyKind(19:00 VN, Utc) = 19:00 UTC = 02:00 VN hôm sau, link sống 7 giờ sau giờ chơi
        var oldBuggy = DateTime.SpecifyKind(new DateTime(2026, 11, 20, 19, 0, 0), DateTimeKind.Utc);

        var expiry = CancelTokenHelper.ComputeExpiry(Vn(2026, 11, 20, 19, 0), Now);

        Assert.Equal(TimeSpan.FromHours(7), oldBuggy - expiry);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ComputeExpiry_WhenPlayIsTwoDaysLater_ExpiresTwentyFourHoursAfterNow()
    {
        var expiry = CancelTokenHelper.ComputeExpiry(Vn(2026, 11, 22, 19, 0), Now);

        Assert.Equal(Now.AddHours(24), expiry);
    }

    [Theory]
    [Trait("Catalog", "BE-UT-12")]
    [InlineData(-1, -1)]   // 23:59:59 trước mốc 24h ⇒ lấy giờ chơi
    [InlineData(0, 0)]     // đúng 24h ⇒ hai mốc bằng nhau
    [InlineData(1, 0)]     // 24h + 1 giây ⇒ lấy now + 24h
    public void ComputeExpiry_AroundTheTwentyFourHourBoundary(int playOffsetSeconds, int expectedOffsetSeconds)
    {
        var limit = Now.AddHours(24);

        var expiry = CancelTokenHelper.ComputeExpiry(limit.AddSeconds(playOffsetSeconds), Now);

        Assert.Equal(limit.AddSeconds(expectedOffsetSeconds), expiry);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ComputeExpiry_WhenBookedAt0659ForSixOClockNextDay_ExpiresAtPlayStart()
    {
        // 06:59 VN ngày 20 (23:59 UTC ngày 19) đặt cho 06:00 VN ngày 21: cách 23 giờ 01 phút, khác ngày VN nhưng trong 24h
        var now = Vn(2026, 11, 20, 6, 59);

        var expiry = CancelTokenHelper.ComputeExpiry(Vn(2026, 11, 21, 6, 0), now);

        Assert.Equal(new DateTime(2026, 11, 20, 23, 0, 0, DateTimeKind.Utc), expiry);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ComputeExpiry_AcrossVietnamMidnight_ExpiresAtPlayStart()
    {
        // 23:59 VN ngày 20 đặt cho 00:00 VN ngày 21: hạn đúng 00:00 VN = 17:00 UTC ngày 20
        var now = Vn(2026, 11, 20, 23, 59);

        var expiry = CancelTokenHelper.ComputeExpiry(Vn(2026, 11, 21, 0, 0), now);

        Assert.Equal(new DateTime(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc), expiry);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ComputeExpiry_WhenPlayAlreadyStarted_ExpiresAtPlayStartSoLinkIsDead()
    {
        // Đơn tại quầy cho khách đang chơi: link sinh ra đã hết hạn, không cho tự hủy sau giờ bắt đầu
        var playStart = Now.AddMinutes(-30);

        Assert.Equal(playStart, CancelTokenHelper.ComputeExpiry(playStart, Now));
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void ComputeExpiry_ForManyRandomPairs_NeverAfterPlayStartNorAfterTwentyFourHours()
    {
        var random = new Random(20261022);
        for (var i = 0; i < 2000; i++)
        {
            var now = Now.AddMinutes(random.Next(-525_600, 525_600));
            var playStart = now.AddMinutes(random.Next(-600, 4 * 1440));

            var expiry = CancelTokenHelper.ComputeExpiry(playStart, now);

            Assert.True(expiry <= playStart, $"now={now:o}, play={playStart:o}");
            Assert.True(expiry <= now.AddHours(24), $"now={now:o}, play={playStart:o}");
            Assert.True(expiry == playStart || expiry == now.AddHours(24), $"now={now:o}, play={playStart:o}");
            Assert.Equal(DateTimeKind.Utc, expiry.Kind);
        }
    }

    [Theory]
    [Trait("Catalog", "BE-UT-12")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ComputeExpiry_WhenPlayStartIsNotUtc_Throws(DateTimeKind kind)
    {
        // T-3: giờ VN dạng Unspecified đi vào phép so sánh bỏ qua Kind rồi bị Npgsql từ chối; giờ đây bị chặn ngay ở đây
        var ex = Assert.Throws<ArgumentException>(() =>
            CancelTokenHelper.ComputeExpiry(DateTime.SpecifyKind(Now.AddHours(2), kind), Now));
        Assert.Equal("playStartUtc", ex.ParamName);
    }

    [Theory]
    [Trait("Catalog", "BE-UT-12")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ComputeExpiry_WhenNowIsNotUtc_Throws(DateTimeKind kind)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CancelTokenHelper.ComputeExpiry(Now.AddHours(2), DateTime.SpecifyKind(Now, kind)));
        Assert.Equal("nowUtc", ex.ParamName);
    }

    // ───────────────────────── Băm token ─────────────────────────

    [Fact]
    public void Hash_IsLowercaseHexSha256()
    {
        // Vector chuẩn SHA-256("abc"): tra cứu GetByCancelTokenAsync dựa vào đúng định dạng này
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", CancelTokenHelper.Hash("abc"));
    }

    [Fact]
    public void Hash_IsDeterministicAndDiffersPerToken()
    {
        Assert.Equal(CancelTokenHelper.Hash("token-1"), CancelTokenHelper.Hash("token-1"));
        Assert.NotEqual(CancelTokenHelper.Hash("token-1"), CancelTokenHelper.Hash("token-2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hash_WhenTokenIsEmpty_Throws(string? token)
    {
        Assert.ThrowsAny<ArgumentException>(() => CancelTokenHelper.Hash(token!));
    }

    // ───────────────────────── Sinh token ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void Generate_ReturnsUrlSafeTokenItsHashAndExpiry()
    {
        var playStart = Vn(2026, 11, 20, 19, 0);

        var token = CancelTokenHelper.Generate(playStart, Now);

        // 32 byte ngẫu nhiên, base64 bỏ đệm ⇒ 43 ký tự; an toàn trên URL ({FrontendBaseUrl}/booking/cancel?token=...)
        Assert.Equal(43, token.RawToken.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token.RawToken);
        Assert.Equal(CancelTokenHelper.Hash(token.RawToken), token.TokenHash);
        Assert.NotEqual(token.RawToken, token.TokenHash);
        Assert.Equal(CancelTokenHelper.ComputeExpiry(playStart, Now), token.ExpiresAtUtc);
    }

    [Fact]
    public void Generate_ProducesDistinctTokens()
    {
        var tokens = Enumerable.Range(0, 1000)
            .Select(_ => CancelTokenHelper.Generate(Now.AddHours(5), Now).RawToken)
            .ToHashSet();

        Assert.Equal(1000, tokens.Count);
    }

    [Fact]
    public void Generate_WhenTimeIsNotUtc_ThrowsBeforeCreatingToken()
    {
        Assert.Throws<ArgumentException>(() =>
            CancelTokenHelper.Generate(DateTime.SpecifyKind(Now.AddHours(5), DateTimeKind.Unspecified), Now));
    }
}
