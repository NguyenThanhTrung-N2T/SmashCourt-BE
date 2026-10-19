using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

public sealed class TestDataSeeder
{
    private readonly SmashCourtContext _context;

    public TestDataSeeder(SmashCourtContext context)
    {
        _context = context;
    }

    public async Task<BasicSeedData> SeedBasicDataAsync()
    {
        var now = DateTime.UtcNow;
        var bronzeTier = new LoyaltyTier
        {
            Id = Guid.NewGuid(),
            Name = $"Bronze-{Guid.NewGuid():N}",
            MinPoints = 0,
            DiscountRate = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        var silverTier = new LoyaltyTier
        {
            Id = Guid.NewGuid(),
            Name = $"Silver-{Guid.NewGuid():N}",
            MinPoints = 100,
            DiscountRate = 5,
            CreatedAt = now,
            UpdatedAt = now
        };
        var timeSlot = new TimeSlot
        {
            Id = Guid.NewGuid(),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            DayType = DayType.WEEKDAY,
            CreatedAt = now
        };
        var courtType = new CourtType
        {
            Id = Guid.NewGuid(),
            Name = $"Single-{Guid.NewGuid():N}",
            Description = "Integration test court type",
            Status = CourtTypeStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.AddRange(bronzeTier, silverTier, timeSlot, courtType);
        await _context.SaveChangesAsync();

        return new BasicSeedData(bronzeTier, silverTier, timeSlot, courtType);
    }

    public async Task<BranchSeedData> SeedBranchWithCourtAsync(CourtType? courtType = null)
    {
        courtType ??= await _context.CourtTypes.FirstOrDefaultAsync(x => x.Status == CourtTypeStatus.ACTIVE)
            ?? (await SeedBasicDataAsync()).CourtType;

        var now = DateTime.UtcNow;
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = $"Test Branch-{Guid.NewGuid():N}",
            Address = "123 Test Street",
            Phone = "0900000000",
            OpenTime = new TimeOnly(6, 0),
            CloseTime = new TimeOnly(23, 0),
            Status = BranchStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        var court = new Court
        {
            Id = Guid.NewGuid(),
            BranchId = branch.Id,
            CourtTypeId = courtType.Id,
            Name = $"Court-{Guid.NewGuid():N}",
            Status = CourtStatus.AVAILABLE,
            CreatedAt = now,
            UpdatedAt = now
        };
        var branchCourtType = new BranchCourtType
        {
            Id = Guid.NewGuid(),
            BranchId = branch.Id,
            CourtTypeId = courtType.Id,
            IsActive = true,
            CreatedAt = now
        };

        _context.AddRange(branch, court, branchCourtType);
        await _context.SaveChangesAsync();

        return new BranchSeedData(branch, court, courtType);
    }
}

public sealed record BasicSeedData(
    LoyaltyTier BronzeTier,
    LoyaltyTier SilverTier,
    TimeSlot TimeSlot,
    CourtType CourtType);

public sealed record BranchSeedData(
    Branch Branch,
    Court Court,
    CourtType CourtType);
