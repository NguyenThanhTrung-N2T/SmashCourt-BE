using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.AccessControl;

namespace SmashCourt_BE.Tests.Services;

public class BranchPriceServiceTests
{
    [Fact]
    public async Task GetEffectivePricesAsync_WhenRoleIsInvalid_ThrowsForbidden()
    {
        var exception = await Assert.ThrowsAsync<AppException>(() => CreateService().GetEffectivePricesAsync(
            Guid.NewGuid(), new DateOnly(2026, 1, 1), null, Guid.NewGuid(), "INVALID"));

        Assert.Equal(ErrorCodes.Forbidden, exception.ErrorCode);
    }

    [Fact]
    public async Task GetEffectivePricesAsync_WhenNoPricesExist_ReturnsEmptyResponse()
    {
        var branchId = Guid.NewGuid();
        var resolver = new Mock<IBranchScopeResolver>();
        resolver.Setup(x => x.ResolveRequiredBranchIdAsync(
                branchId, It.IsAny<Guid>(), UserRole.OWNER))
            .ReturnsAsync(branchId);
        var branchPrices = new Mock<IBranchPriceRepository>();
        var systemPrices = new Mock<ISystemPriceRepository>();
        branchPrices.Setup(x => x.GetCurrentForDateAsync(branchId, It.IsAny<DateOnly>(), null))
            .ReturnsAsync([]);
        systemPrices.Setup(x => x.GetCurrentForDateAsync(It.IsAny<DateOnly>(), null))
            .ReturnsAsync([]);

        var result = await CreateService(branchPrices, systemPrices, resolver)
            .GetEffectivePricesAsync(branchId, new DateOnly(2026, 1, 1), null, Guid.NewGuid(), "OWNER");

        Assert.Equal(branchId, result.BranchId);
        Assert.Empty(result.CourtTypes);
    }

    [Fact]
    public async Task GetEffectivePricesAsync_WhenBranchOverrideExists_UsesBranchPrices()
    {
        var branchId = Guid.NewGuid();
        var courtTypeId = Guid.NewGuid();
        var slotId = Guid.NewGuid();
        var slot = new TimeSlot
        {
            Id = slotId,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            DayType = DayType.WEEKDAY
        };
        var courtType = new CourtType { Id = courtTypeId, Name = "Standard" };
        var resolver = new Mock<IBranchScopeResolver>();
        resolver.Setup(x => x.ResolveRequiredBranchIdAsync(
                branchId, It.IsAny<Guid>(), UserRole.OWNER)).ReturnsAsync(branchId);
        var branchPrices = new Mock<IBranchPriceRepository>();
        var systemPrices = new Mock<ISystemPriceRepository>();
        branchPrices.Setup(x => x.GetCurrentForDateAsync(branchId, It.IsAny<DateOnly>(), courtTypeId))
            .ReturnsAsync([
                new BranchPriceOverride
                {
                    BranchId = branchId, CourtTypeId = courtTypeId, TimeSlotId = slotId,
                    Price = 120000m, EffectiveFrom = new DateOnly(2026, 1, 1),
                    TimeSlot = slot, CourtType = courtType
                }
            ]);
        systemPrices.Setup(x => x.GetCurrentForDateAsync(It.IsAny<DateOnly>(), courtTypeId))
            .ReturnsAsync([
                new SystemPrice
                {
                    CourtTypeId = courtTypeId, TimeSlotId = slotId, Price = 90000m,
                    EffectiveFrom = new DateOnly(2026, 1, 1), TimeSlot = slot, CourtType = courtType
                }
            ]);

        var result = await CreateService(branchPrices, systemPrices, resolver)
            .GetEffectivePricesAsync(branchId, new DateOnly(2026, 1, 1), courtTypeId, Guid.NewGuid(), "OWNER");

        var effective = Assert.Single(Assert.Single(result.CourtTypes).Slots);
        Assert.Equal(120000m, effective.WeekdayPrice);
        Assert.Equal("BRANCH", effective.PriceSource);
    }

    [Fact]
    public async Task GetEffectivePricesAsync_WhenNoBranchOverride_UsesSystemPrice()
    {
        var branchId = Guid.NewGuid();
        var courtTypeId = Guid.NewGuid();
        var slot = new TimeSlot
        {
            Id = Guid.NewGuid(), StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0),
            DayType = DayType.WEEKDAY
        };
        var courtType = new CourtType { Id = courtTypeId, Name = "Standard" };
        var resolver = new Mock<IBranchScopeResolver>();
        resolver.Setup(x => x.ResolveRequiredBranchIdAsync(
                branchId, It.IsAny<Guid>(), UserRole.OWNER)).ReturnsAsync(branchId);
        var branchPrices = new Mock<IBranchPriceRepository>();
        var systemPrices = new Mock<ISystemPriceRepository>();
        branchPrices.Setup(x => x.GetCurrentForDateAsync(branchId, It.IsAny<DateOnly>(), courtTypeId))
            .ReturnsAsync([]);
        systemPrices.Setup(x => x.GetCurrentForDateAsync(It.IsAny<DateOnly>(), courtTypeId))
            .ReturnsAsync([
                new SystemPrice
                {
                    CourtTypeId = courtTypeId, TimeSlotId = slot.Id, Price = 90000m,
                    EffectiveFrom = new DateOnly(2026, 1, 1), TimeSlot = slot, CourtType = courtType
                }
            ]);

        var result = await CreateService(branchPrices, systemPrices, resolver)
            .GetEffectivePricesAsync(branchId, new DateOnly(2026, 1, 1), courtTypeId, Guid.NewGuid(), "OWNER");

        var effective = Assert.Single(Assert.Single(result.CourtTypes).Slots);
        Assert.Equal(90000m, effective.WeekdayPrice);
        Assert.Equal("SYSTEM", effective.PriceSource);
    }

    private static BranchPriceService CreateService(
        Mock<IBranchPriceRepository>? branchPrices = null,
        Mock<ISystemPriceRepository>? systemPrices = null,
        Mock<IBranchScopeResolver>? resolver = null) =>
        new(
            (branchPrices ?? new Mock<IBranchPriceRepository>()).Object,
            (systemPrices ?? new Mock<ISystemPriceRepository>()).Object,
            Mock.Of<ITimeSlotRepository>(),
            Mock.Of<IBranchRepository>(),
            Mock.Of<ICourtRepository>(),
            (resolver ?? new Mock<IBranchScopeResolver>()).Object);
}
