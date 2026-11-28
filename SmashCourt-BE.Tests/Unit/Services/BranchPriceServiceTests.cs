using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.AccessControl;

namespace SmashCourt_BE.Tests.Unit.Services;

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

    // ─── Tiền sân tính theo tỷ lệ phần khung giờ: luôn là số đồng nguyên ─────────

    // Khung giá (giờ bắt đầu, giờ kết thúc, giá cả khung) và lượt đặt; giá ghi theo cả khung, đặt một phần thì tính theo tỷ lệ
    [Theory]
    [InlineData("17:00", "20:00", 100_000, "17:00", "18:00", 33_333)]   // 1/3 khung 3 giờ = 33.333,33
    [InlineData("17:00", "20:00", 100_000, "17:00", "19:00", 66_667)]   // 2/3 = 66.666,67
    [InlineData("17:00", "20:00", 100_000, "17:00", "20:00", 100_000)]  // nguyên khung
    [InlineData("10:00", "11:00", 85_001, "10:00", "10:30", 42_501)]    // nửa giờ của giá lẻ = 42.500,5
    [InlineData("10:00", "11:00", 85_000, "10:00", "10:30", 42_500)]
    [InlineData("06:00", "07:30", 100_000, "06:00", "06:30", 33_333)]   // 1/3 khung 1,5 giờ
    public async Task CalculateForBookingAsync_WhenBookingCoversPartOfASlot_ChargesWholeDong(
        string slotStart, string slotEnd, int slotPrice, string bookStart, string bookEnd, int expected)
    {
        var result = await CalculateAsync(
            [(TimeOnly.Parse(slotStart), TimeOnly.Parse(slotEnd), slotPrice)], TimeOnly.Parse(bookStart), TimeOnly.Parse(bookEnd));

        var court = Assert.Single(result.Courts);
        Assert.Equal(expected, court.CourtFee);
        Assert.Equal(expected, result.TotalFee);
        Assert.Equal(expected, Assert.Single(court.Breakdown).SubTotal);
    }

    [Fact]
    public async Task CalculateForBookingAsync_WhenBookingSpansSlots_EachPartIsWholeAndTheyAddUp()
    {
        // 18:00–19:00 = 1/3 khung 17:00–18:30 (33.333,33) + 1/2 khung 18:30–19:30 giá lẻ (45.000,5)
        var result = await CalculateAsync(
            [(new TimeOnly(17, 0), new TimeOnly(18, 30), 100_000), (new TimeOnly(18, 30), new TimeOnly(19, 30), 90_001)],
            new TimeOnly(18, 0), new TimeOnly(19, 0));

        var court = Assert.Single(result.Courts);
        Assert.Equal([33_333m, 45_001m], court.Breakdown.Select(b => b.SubTotal));
        Assert.Equal(78_334m, court.CourtFee);
        Assert.Equal(court.Breakdown.Sum(b => b.SubTotal), court.CourtFee);
    }

    [Fact]
    public async Task CalculateAsync_PreviewShowsTheSameWholeTotalAsTheBooking()
    {
        (TimeOnly, TimeOnly, int)[] slots = [(new TimeOnly(17, 0), new TimeOnly(20, 0), 100_000)];

        var preview = await CalculateAsync(slots, new TimeOnly(17, 0), new TimeOnly(18, 0), forBooking: false);
        var booking = await CalculateAsync(slots, new TimeOnly(17, 0), new TimeOnly(18, 0));

        Assert.Equal(33_333m, preview.TotalFee);
        Assert.Equal(booking.TotalFee, preview.TotalFee);
    }

    private static async Task<SmashCourt_BE.DTOs.PriceConfig.CalculatePriceResultDto> CalculateAsync(
        (TimeOnly Start, TimeOnly End, int Price)[] slots, TimeOnly bookStart, TimeOnly bookEnd, bool forBooking = true)
    {
        var branchId = Guid.NewGuid();
        var courtType = new CourtType { Id = Guid.NewGuid(), Name = "Standard" };
        var court = new Court
        {
            Id = Guid.NewGuid(), BranchId = branchId, CourtTypeId = courtType.Id, Name = "Sân 1", Status = CourtStatus.AVAILABLE
        };
        var timeSlots = slots
            .Select(s => new TimeSlot { Id = Guid.NewGuid(), StartTime = s.Start, EndTime = s.End, DayType = DayType.WEEKDAY })
            .ToList();
        var systemPriceRows = timeSlots.Zip(slots, (slot, s) => new SystemPrice
        {
            CourtTypeId = courtType.Id, TimeSlotId = slot.Id, Price = s.Price, EffectiveFrom = new DateOnly(2020, 1, 1),
            TimeSlot = slot, CourtType = courtType
        }).ToList();

        var courts = new Mock<ICourtRepository>();
        courts.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync([court]);
        var branchPrices = new Mock<IBranchPriceRepository>();
        branchPrices.Setup(x => x.GetCurrentForDateAsync(branchId, It.IsAny<DateOnly>(), courtType.Id)).ReturnsAsync([]);
        var systemPrices = new Mock<ISystemPriceRepository>();
        systemPrices.Setup(x => x.GetCurrentForDateAsync(It.IsAny<DateOnly>(), courtType.Id)).ReturnsAsync(systemPriceRows);
        var timeSlotRepo = new Mock<ITimeSlotRepository>();
        timeSlotRepo.Setup(x => x.GetByDayTypeAsync(It.IsAny<DayType>())).ReturnsAsync(timeSlots);

        var service = new BranchPriceService(
            branchPrices.Object, systemPrices.Object, timeSlotRepo.Object, Mock.Of<IBranchRepository>(), courts.Object,
            Mock.Of<IBranchScopeResolver>());
        var dto = new SmashCourt_BE.DTOs.PriceConfig.CalculatePriceDto
        {
            Courts = [court.Id],
            BookingDate = SmashCourt_BE.Helpers.DateTimeHelper.GetTodayInVietnam().AddDays(3).ToDateTime(TimeOnly.MinValue),
            StartTime = bookStart.ToTimeSpan(),
            EndTime = bookEnd.ToTimeSpan()
        };

        return forBooking ? await service.CalculateForBookingAsync(branchId, dto) : await service.CalculateAsync(branchId, dto);
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
