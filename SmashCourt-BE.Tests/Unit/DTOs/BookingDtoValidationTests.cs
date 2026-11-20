using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.DTOs;

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

    // Mốc lẻ chỉ tới khi gọi API trực tiếp (FE chọn theo bước 30 phút): 20 phút, lệch 15 phút ở đầu / cuối, lệch giây
    public static TheoryData<TimeSpan, TimeSpan> OffStepSlots => new()
    {
        { new TimeSpan(21, 40, 0), new TimeSpan(22, 0, 0) },
        { new TimeSpan(18, 15, 0), new TimeSpan(19, 0, 0) },
        { new TimeSpan(18, 0, 0), new TimeSpan(18, 45, 0) },
        { new TimeSpan(18, 0, 30), new TimeSpan(19, 0, 0) },
        { new TimeSpan(18, 0, 0), new TimeSpan(18, 10, 0) }
    };

    public static TheoryData<TimeSpan, TimeSpan> OnStepSlots => new()
    {
        { new TimeSpan(21, 30, 0), new TimeSpan(22, 0, 0) },
        { new TimeSpan(18, 0, 0), new TimeSpan(19, 30, 0) },
        { new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0) } // đúng 12 giờ: còn nhận
    };

    // Dài hơn 720 phút: TADSS từ chối item nên BE chặn trước (P6-61)
    public static TheoryData<TimeSpan, TimeSpan> TooLongSlots => new()
    {
        { new TimeSpan(6, 0, 0), new TimeSpan(22, 0, 0) },
        { new TimeSpan(8, 0, 0), new TimeSpan(20, 30, 0) }
    };

    [Theory]
    [MemberData(nameof(TooLongSlots))]
    public void CreateOnlineBookingDto_WhenLongerThanTwelveHours_ReturnsValidationError(TimeSpan start, TimeSpan end)
    {
        var dto = CreateDto(new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = start, EndTime = end });

        var error = Assert.Single(Validate(dto));
        Assert.Contains("tối đa 12 giờ", error.ErrorMessage);
    }

    [Theory]
    [MemberData(nameof(TooLongSlots))]
    public void CreateWalkInBookingDto_WhenLongerThanTwelveHours_ReturnsValidationError(TimeSpan start, TimeSpan end)
    {
        var dto = CreateWalkInDto(new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = start, EndTime = end });

        var error = Assert.Single(dto.Validate(new ValidationContext(dto)));
        Assert.Contains("tối đa 12 giờ", error.ErrorMessage);
    }

    [Theory]
    [MemberData(nameof(OffStepSlots))]
    public void CreateOnlineBookingDto_WhenTimeIsOffThirtyMinuteStep_ReturnsValidationError(TimeSpan start, TimeSpan end)
    {
        var dto = CreateDto(new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = start, EndTime = end });

        var errors = Validate(dto);

        Assert.Contains(errors, error => error.ErrorMessage!.Contains("bước 30 phút"));
    }

    [Theory]
    [MemberData(nameof(OnStepSlots))]
    public void CreateOnlineBookingDto_WhenTimeIsOnThirtyMinuteStep_ReturnsNoError(TimeSpan start, TimeSpan end)
    {
        var dto = CreateDto(new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = start, EndTime = end });

        Assert.Empty(Validate(dto));
    }

    [Fact]
    public void CreateOnlineBookingDto_WhenStartTimeIsNotBeforeEndTime_DoesNotAlsoReportStepError()
    {
        var dto = CreateDto(new CourtSlotDto
        {
            CourtId = Guid.NewGuid(),
            StartTime = new TimeSpan(19, 15, 0),
            EndTime = new TimeSpan(18, 0, 0)
        });

        var error = Assert.Single(Validate(dto));
        Assert.Contains("Giờ bắt đầu phải nhỏ hơn", error.ErrorMessage);
    }

    [Theory]
    [MemberData(nameof(OffStepSlots))]
    public void CreateWalkInBookingDto_WhenTimeIsOffThirtyMinuteStep_ReturnsValidationError(TimeSpan start, TimeSpan end)
    {
        var dto = CreateWalkInDto(new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = start, EndTime = end });

        var errors = dto.Validate(new ValidationContext(dto)).ToList();

        Assert.Contains(errors, error => error.ErrorMessage!.Contains("bước 30 phút"));
    }

    [Theory]
    [MemberData(nameof(OnStepSlots))]
    public void CreateWalkInBookingDto_WhenTimeIsOnThirtyMinuteStep_ReturnsNoError(TimeSpan start, TimeSpan end)
    {
        var dto = CreateWalkInDto(new CourtSlotDto { CourtId = Guid.NewGuid(), StartTime = start, EndTime = end });

        Assert.Empty(dto.Validate(new ValidationContext(dto)));
    }

    [Theory]
    [InlineData(18, 0, 0, true)]
    [InlineData(18, 30, 0, true)]
    [InlineData(0, 0, 0, true)]
    [InlineData(18, 29, 0, false)]
    [InlineData(18, 30, 1, false)]
    public void CourtSlotDto_IsOnSlotStep_ChecksBothEnds(int hour, int minute, int second, bool expected)
    {
        var time = new TimeSpan(hour, minute, second);
        var asStart = new CourtSlotDto { StartTime = time, EndTime = new TimeSpan(23, 0, 0) };
        var asEnd = new CourtSlotDto { StartTime = new TimeSpan(0, 0, 0), EndTime = time };

        Assert.Equal(expected, asStart.IsOnSlotStep());
        Assert.Equal(expected, asEnd.IsOnSlotStep());
    }

    // Ngày tương lai để chỉ còn lỗi đang kiểm (DTO chặn ngày trong quá khứ theo giờ VN)
    private static DateTime Tomorrow =>
        DateTimeHelper.GetTodayInVietnam().AddDays(1).ToDateTime(TimeOnly.MinValue);

    private static List<ValidationResult> Validate(CreateOnlineBookingDto dto)
    {
        var context = new ValidationContext(dto);
        return dto.Validate(context).ToList();
    }

    private static CreateOnlineBookingDto CreateDto(params CourtSlotDto[] slots) => new()
    {
        BookingDate = Tomorrow,
        Courts = slots.ToList()
    };

    private static CreateWalkInBookingDto CreateWalkInDto(params CourtSlotDto[] slots) => new()
    {
        BookingDate = Tomorrow,
        Courts = slots.ToList(),
        GuestName = "Khách vãng lai",
        GuestPhone = "0900000000"
    };
}