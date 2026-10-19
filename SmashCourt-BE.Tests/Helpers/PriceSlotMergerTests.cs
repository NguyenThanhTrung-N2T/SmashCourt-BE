using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Tests.Helpers;

public class PriceSlotMergerTests
{
    [Fact]
    public void MergeConsecutivePriceSlots_MergesAdjacentSlotsWithSamePrices()
    {
        var courtTypeId = Guid.NewGuid();
        var slots = new List<CurrentPriceDto>
        {
            new() { CourtTypeId = courtTypeId, CourtTypeName = "Standard", StartTime = new(17, 0, 0), EndTime = new(18, 0, 0), WeekdayPrice = 100_000, WeekendPrice = 120_000, EffectiveFrom = "2026-01-01" },
            new() { CourtTypeId = courtTypeId, CourtTypeName = "Standard", StartTime = new(18, 0, 0), EndTime = new(19, 0, 0), WeekdayPrice = 100_000, WeekendPrice = 120_000, EffectiveFrom = "2026-01-01" }
        };

        var result = PriceSlotMerger.MergeConsecutivePriceSlots(slots);

        var merged = Assert.Single(result);
        Assert.Equal(new TimeSpan(17, 0, 0), merged.StartTime);
        Assert.Equal(new TimeSpan(19, 0, 0), merged.EndTime);
    }

    [Fact]
    public void MergeConsecutivePriceSlots_DoesNotMergeDifferentPrices()
    {
        var courtTypeId = Guid.NewGuid();
        var slots = new List<CurrentPriceDto>
        {
            new() { CourtTypeId = courtTypeId, CourtTypeName = "Standard", StartTime = new(17, 0, 0), EndTime = new(18, 0, 0), WeekdayPrice = 100_000, WeekendPrice = 120_000, EffectiveFrom = "2026-01-01" },
            new() { CourtTypeId = courtTypeId, CourtTypeName = "Standard", StartTime = new(18, 0, 0), EndTime = new(19, 0, 0), WeekdayPrice = 150_000, WeekendPrice = 120_000, EffectiveFrom = "2026-01-01" }
        };

        var result = PriceSlotMerger.MergeConsecutivePriceSlots(slots);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MergeConsecutivePriceSlots_EmptyInputReturnsEmptyList()
    {
        Assert.Empty(PriceSlotMerger.MergeConsecutivePriceSlots([]));
        Assert.Empty(PriceSlotMerger.MergeConsecutivePriceSlots(null!));
    }
}
