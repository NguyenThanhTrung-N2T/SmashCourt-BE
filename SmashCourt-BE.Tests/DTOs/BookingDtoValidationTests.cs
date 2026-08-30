using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.DTOs;

[Trait("Category", TestCategories.Booking)]
public class BookingDtoValidationTests
{
    [Fact]
    public void CreateOnlineBookingDto_WhenExactDuplicateSlotExists_ReturnsValidationError()
    {
        var courtId = Guid.NewGuid();
        var dto = CreateDto(
            new CourtSlotDto { CourtId = courtId, StartTime = TestConstants.EveningStartTime, EndTime = TestConstants.EveningEndTime },
            new CourtSlotDto { CourtId = courtId, StartTime = TestConstants.EveningStartTime, EndTime = TestConstants.EveningEndTime });

        var errors = Validate(dto);

        Assert.Contains(errors, error => error.ErrorMessage!.Contains("trùng lặp hoàn toàn"));
    }

    [Fact]
    public void CreateOnlineBookingDto_WhenCourtsUseDifferentTimeSlots_ReturnsValidationError()
    {
        var dto = CreateDto(
            new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = TestConstants.EveningStartTime, EndTime = TestConstants.EveningEndTime },
            new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = TestConstants.MorningStartTime, EndTime = TestConstants.MorningEndTime });

        var errors = Validate(dto);

        Assert.Contains(errors, error => error.ErrorMessage!.Contains("dùng chung một mốc giờ"));
    }

    [Fact]
    public void CreateOnlineBookingDto_WhenStartTimeIsNotBeforeEndTime_ReturnsValidationError()
    {
        var dto = CreateDto(new CourtSlotDto
        {
            CourtId = Guid.NewGuid(),
            StartTime = TestConstants.EveningEndTime,
            EndTime = TestConstants.EveningStartTime
        });

        var errors = Validate(dto);

        Assert.Contains(errors, error => error.ErrorMessage!.Contains("Giờ bắt đầu"));
    }

    private static List<ValidationResult> Validate(CreateOnlineBookingDto dto)
    {
        var context = new ValidationContext(dto);
        return dto.Validate(context).ToList();
    }

    private static CreateOnlineBookingDto CreateDto(params CourtSlotDto[] slots) => new()
    {
        BookingDate = TestConstants.StandardDateTime,
        Courts = slots.ToList()
    };
}