using System.Text.Json.Nodes;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Helpers;

/// <summary>BE-UT-02 và BE-UT-12 (phần request): ánh xạ booking sang request v2, thời gian luôn có offset và đúng thời điểm.</summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-02")]
public class TadssDecisionRequestMapperTests
{
    private static readonly Guid BookingId = Guid.Parse("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11");
    private static readonly Guid BranchId = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000001");
    private static readonly Guid CustomerId = Guid.Parse("9d3f1c2a-0000-4000-8000-000000000123");
    private static readonly Guid CourtA = Guid.Parse("c0a8012e-0000-4000-8000-0000000000aa");
    private static readonly Guid CourtB = Guid.Parse("c0a8012e-0000-4000-8000-0000000000ab");
    private static readonly Guid CourtType = Guid.Parse("c0a8012e-0000-4000-8000-0000000000bb");

    private static readonly TadssSettings Settings = new() { BranchTimeZone = "Asia/Ho_Chi_Minh" };
    private static readonly CustomerHistorySummary EmptyHistory = new(0, 0, 0, 0, 0, null);

    private static BookingDecisionSnapshot Snapshot(
        DateTime? createdAt = null,
        int? tierRank = null,
        string? promotionCode = null,
        IReadOnlyList<BookingDecisionCourt>? courts = null,
        decimal finalTotal = 228_000m) => new(
            BookingId, BranchId, CustomerId,
            createdAt ?? new DateTime(2026, 11, 18, 2, 15, 0, DateTimeKind.Utc),
            finalTotal, promotionCode,
            new DateTime(2025, 3, 14, 1, 0, 0, DateTimeKind.Utc), tierRank,
            courts ?? [new BookingDecisionCourt(CourtA, CourtType, new DateOnly(2026, 11, 20), new TimeOnly(18, 0), new TimeOnly(20, 0), 240_000m)]);

    private static TadssDecisionRequestDto Map(BookingDecisionSnapshot snapshot, CustomerHistorySummary? history = null, TadssSettings? settings = null) =>
        TadssDecisionRequestMapper.Map(snapshot, history ?? EmptyHistory, settings ?? Settings);

    private static string Serialize(TadssDecisionRequestDto request) => System.Text.Json.JsonSerializer.Serialize(request, TadssJson.Options);

    // ── Ánh xạ trường (BE-UT-02) ─────────────────────────────────────────────────

    [Fact]
    public void Map_WhenTwoCourts_ProducesOneItemPerCourtWithPriceBeforeDiscountAndTotalAfterDiscount()
    {
        var courts = new[]
        {
            new BookingDecisionCourt(CourtA, CourtType, new DateOnly(2026, 11, 20), new TimeOnly(18, 0), new TimeOnly(20, 0), 110_000m),
            new BookingDecisionCourt(CourtB, CourtType, new DateOnly(2026, 11, 20), new TimeOnly(18, 0), new TimeOnly(20, 0), 80_000m)
        };

        var request = Map(Snapshot(courts: courts, finalTotal: 180_000m));

        Assert.Equal(2, request.Items.Count);
        Assert.Equal([CourtA, CourtB], request.Items.Select(i => i.ResourceId));
        Assert.All(request.Items, i => Assert.Equal(CourtType, i.ResourceTypeId));
        Assert.Equal([110_000m, 80_000m], request.Items.Select(i => i.Price));
        Assert.Equal(180_000m, request.TotalPrice); // sau giảm giá, khác tổng giá sân 190.000
    }

    [Fact]
    public void Map_WhenOnlineBooking_SetsFixedFields()
    {
        var request = Map(Snapshot());

        Assert.Equal(BookingId, request.ReservationId);
        Assert.Equal(BranchId, request.BranchId);
        Assert.Equal("Asia/Ho_Chi_Minh", request.BranchTimeZone);
        Assert.Equal("VND", request.Currency);
        Assert.Equal(TadssChannel.ONLINE, request.Channel);
        Assert.Null(request.Demand);
        Assert.False(request.Customer.IsGuest);
        Assert.Equal(CustomerId, request.Customer.CustomerId);
    }

    [Theory]
    [InlineData(null, TadssTier.STANDARD)]
    [InlineData(0, TadssTier.STANDARD)]
    [InlineData(1, TadssTier.STANDARD)]
    [InlineData(2, TadssTier.SILVER)]
    [InlineData(3, TadssTier.GOLD)]
    [InlineData(4, TadssTier.PLATINUM)]
    [InlineData(5, TadssTier.DIAMOND)]
    [InlineData(6, TadssTier.DIAMOND)]
    [InlineData(12, TadssTier.DIAMOND)]
    public void MapTier_WhenRankGiven_FollowsRankOrderNotTierName(int? rank, TadssTier expected)
    {
        Assert.Equal(expected, TadssDecisionRequestMapper.MapTier(rank));
        Assert.Equal(expected, Map(Snapshot(tierRank: rank)).Customer.Tier);
    }

    [Fact]
    public void Map_WhenHistoryGiven_CopiesCountsAndKeepsEvenZeroCounts()
    {
        var request = Map(Snapshot(), new CustomerHistorySummary(12, 1, 2, 3, 4, null));

        var history = request.Customer.History!;
        Assert.Equal(12, history.CompletedCount);
        Assert.Equal(1, history.NoShowCount);
        Assert.Equal(2, history.CancelledCount);
        Assert.Equal(3, history.LateCancelledCount);
        Assert.Equal(4, history.PaymentExpiredCount);
        Assert.Null(history.LastCompletedAt);
        Assert.Equal(0, Map(Snapshot()).Customer.History!.PaymentExpiredCount); // khách mới vẫn gửi history toàn số 0
    }

    [Theory]
    [InlineData("WINTER", "WINTER")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Map_WhenPromotionCodeGiven_SetsMetadataOnlyForRealCode(string? code, string? expected)
    {
        var request = Map(Snapshot(promotionCode: code));

        if (expected == null)
            Assert.Null(request.Metadata);
        else
            Assert.Equal(expected, request.Metadata!["promotionCode"]);
    }

    [Fact]
    public void Map_WhenCalledTwiceWithSameInput_ProducesIdenticalJson()
    {
        var snapshot = Snapshot(tierRank: 3, promotionCode: "WINTER");
        var history = new CustomerHistorySummary(5, 1, 0, 2, 0, new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc));

        Assert.Equal(Serialize(Map(snapshot, history)), Serialize(Map(snapshot, history)));
    }

    // ── Hợp lệ theo schema v2 ────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Map_WhenSerialized_IsValidAgainstDecisionRequestSchema(bool withOptionalParts)
    {
        var snapshot = Snapshot(
            tierRank: withOptionalParts ? 4 : null,
            promotionCode: withOptionalParts ? "WINTER" : null,
            courts:
            [
                new BookingDecisionCourt(CourtA, CourtType, new DateOnly(2026, 11, 20), new TimeOnly(18, 0), new TimeOnly(20, 0), 100m),
                new BookingDecisionCourt(CourtB, CourtType, new DateOnly(2026, 11, 20), new TimeOnly(18, 0), new TimeOnly(20, 0), 100m)
            ]);
        var history = withOptionalParts
            ? new CustomerHistorySummary(12, 1, 2, 1, 0, new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc))
            : EmptyHistory;

        var json = Serialize(Map(snapshot, history));

        var result = TadssContractFiles.Evaluate(TadssContractFiles.DecisionRequest, json);
        Assert.True(result.IsValid, TadssContractFiles.Describe(result));
    }

    // ── Thời gian (BE-UT-12, phần request) ───────────────────────────────────────

    [Trait("Catalog", "BE-UT-12")]
    [Fact]
    public void Map_WhenSerialized_EveryTimeFieldCarriesAnOffset()
    {
        var snapshot = Snapshot();
        var json = JsonNode.Parse(Serialize(Map(snapshot, new CustomerHistorySummary(1, 0, 0, 0, 0, new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc)))))!;

        string[] values =
        [
            json["items"]![0]!["startTime"]!.GetValue<string>(), json["items"]![0]!["endTime"]!.GetValue<string>(),
            json["requestedAt"]!.GetValue<string>(), json["customer"]!["memberSince"]!.GetValue<string>(),
            json["customer"]!["history"]!["lastCompletedAt"]!.GetValue<string>()
        ];
        Assert.All(values, v => Assert.Matches(@"(Z|[+-]\d{2}:\d{2})$", v));
    }

    [Trait("Catalog", "BE-UT-12")]
    [Theory]
    [InlineData(0, 30, "2026-11-17T17:30:00Z")]   // 00:30 giờ VN
    [InlineData(6, 59, "2026-11-17T23:59:00Z")]   // 06:59 giờ VN
    [InlineData(23, 45, "2026-11-18T16:45:00Z")]  // 23:45 giờ VN
    [InlineData(23, 55, "2026-11-18T16:55:00Z")]  // 23:55 giờ VN = 16:55Z cùng ngày
    public void Map_WhenBookingCreatedAtVietnamLocalTime_RequestedAtIsTheSameInstant(int hour, int minute, string expectedUtc)
    {
        var created = DateTimeHelper.ToUtcFromVietnam(new DateOnly(2026, 11, 18), new TimeOnly(hour, minute));

        var request = Map(Snapshot(createdAt: created));

        Assert.Equal(DateTimeOffset.Parse(expectedUtc), request.RequestedAt);
        Assert.Equal(TimeSpan.Zero, request.RequestedAt.Offset);
    }

    [Trait("Catalog", "BE-UT-12")]
    [Fact]
    public void Map_WhenCourtPlaysShortlyAfterMidnight_ItemKeepsVietnamDateAndOffset()
    {
        var court = new BookingDecisionCourt(CourtA, CourtType, new DateOnly(2026, 11, 21), new TimeOnly(0, 15), new TimeOnly(1, 15), 100_000m);

        var request = Map(Snapshot(courts: [court]));
        var item = JsonNode.Parse(Serialize(request))!["items"]![0]!;

        Assert.Equal("2026-11-21T00:15:00+07:00", item["startTime"]!.GetValue<string>());
        Assert.Equal("2026-11-21T01:15:00+07:00", item["endTime"]!.GetValue<string>());
    }

    [Trait("Catalog", "BE-UT-12")]
    [Fact]
    public void Map_WhenLastCompletedGiven_IsShownWithBranchOffsetOfTheSameInstant()
    {
        var lastCompleted = new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc); // 20:00 giờ VN

        var request = Map(Snapshot(), new CustomerHistorySummary(1, 0, 0, 0, 0, lastCompleted));

        var value = request.Customer.History!.LastCompletedAt!.Value;
        Assert.Equal(new DateTimeOffset(lastCompleted), value);
        Assert.Equal(TimeSpan.FromHours(7), value.Offset);
        Assert.Equal("2026-11-02T20:00:00+07:00", JsonNode.Parse(Serialize(request))!["customer"]!["history"]!["lastCompletedAt"]!.GetValue<string>());
    }

    [Trait("Catalog", "BE-UT-12")]
    [Fact]
    public void Map_WhenBranchTimeZoneIsNotVietnam_KeepsInstantsAndOnlyChangesOffset()
    {
        var tokyo = new TadssSettings { BranchTimeZone = "Asia/Tokyo" };

        var request = Map(Snapshot(), settings: tokyo);

        var start = request.Items[0].StartTime; // 18:00 giờ VN = 11:00Z = 20:00 giờ Tokyo
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(TimeSpan.FromHours(9), start.Offset);
        Assert.Equal("Asia/Tokyo", request.BranchTimeZone);
    }

    [Trait("Catalog", "BE-UT-12")]
    [Fact]
    public void Map_WhenMomentIsNotUtc_Throws()
    {
        var local = DateTime.SpecifyKind(new DateTime(2026, 11, 18, 9, 15, 0), DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => Map(Snapshot(createdAt: local)));
    }
}
