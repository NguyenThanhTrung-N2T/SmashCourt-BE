using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PostgreSqlSmokeTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Database_WhenFixtureInitializes_CanPersistAndReadTimeSlot()
    {
        var timeSlot = new TimeSlot
        {
            Id = Guid.NewGuid(),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            DayType = DayType.WEEKDAY,
            CreatedAt = DateTime.UtcNow
        };

        DbContext.TimeSlots.Add(timeSlot);
        await DbContext.SaveChangesAsync();

        var persisted = await DbContext.TimeSlots
            .AsNoTracking()
            .SingleAsync(x => x.Id == timeSlot.Id);

        Assert.Equal(timeSlot.StartTime, persisted.StartTime);
        Assert.Equal(timeSlot.EndTime, persisted.EndTime);
        Assert.Equal(DayType.WEEKDAY, persisted.DayType);
    }
}
