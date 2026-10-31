using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;
using SmashCourt_BE.Tests.Integration.Http;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Báo người đã đăng ký "báo khi trống" khi slot được nhả (tách khỏi BookingService ở 6A-3 để từ chối duyệt và job hết hạn duyệt dùng chung;
/// hành vi giữ nguyên): gửi email cho từng người, lỗi một người không chặn người khác, xóa đăng ký sau khi báo (một lần).
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "BE-IT-06")]
public class SlotInterestNotificationServiceTests
{
    private static readonly DateOnly Date = new(2026, 11, 21);
    private static readonly TimeOnly Start = new(18, 0);
    private static readonly TimeOnly End = new(19, 0);

    private readonly Mock<ISlotInterestRepository> _repository = new();
    private readonly RecordingEmailService _email = new(TestConfigurationFactory.Create());

    private SlotInterestNotificationService Create() =>
        new(_repository.Object, _email, TestConfigurationFactory.Create(), NullLogger<SlotInterestNotificationService>.Instance);

    private static Booking BookingOn(params Court[] courts)
    {
        var booking = TestDataFactory.CreateBooking();
        booking.Branch = new Branch { Id = booking.BranchId, Name = "SmashCourt Thủ Đức" };
        foreach (var court in courts)
            booking.BookingCourts.Add(new BookingCourt { CourtId = court.Id, Court = court, Date = Date, StartTime = Start, EndTime = End });
        return booking;
    }

    private static Court Court(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private void Interested(Guid courtId, params string[] emails) =>
        _repository.Setup(x => x.GetOverlappingSlotInterestsAsync(courtId, Date, Start, End))
            .ReturnsAsync(emails.Select(e => new SlotInterest { Id = Guid.NewGuid(), CourtId = courtId, Email = e }).ToList());

    [Fact]
    public async Task NotifiesEveryInterestedPersonThenDeletesTheirInterest()
    {
        var court = Court("Sân 3");
        Interested(court.Id, "a@test.local", "b@test.local");

        await Create().NotifyReleasedSlotsAsync(BookingOn(court));

        Assert.Equal(["a@test.local", "b@test.local"], _email.Sent.Select(e => e.To).OrderBy(x => x));
        Assert.All(_email.Sent, e => Assert.Contains("Sân 3", e.Body));
        Assert.All(_email.Sent, e => Assert.Contains("SmashCourt Thủ Đức", e.Body));
        _repository.Verify(x => x.DeleteOverlappingSlotInterestsAsync(court.Id, Date, Start, End), Times.Once);
    }

    [Fact]
    public async Task CourtWithoutInterest_SendsAndDeletesNothing()
    {
        var court = Court("Sân 1");
        Interested(court.Id);

        await Create().NotifyReleasedSlotsAsync(BookingOn(court));

        Assert.Empty(_email.Sent);
        _repository.Verify(x => x.DeleteOverlappingSlotInterestsAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()), Times.Never);
    }

    [Fact]
    public async Task EachReleasedCourtIsHandledSeparately()
    {
        var first = Court("Sân 1");
        var second = Court("Sân 2");
        Interested(first.Id);
        Interested(second.Id, "c@test.local");

        await Create().NotifyReleasedSlotsAsync(BookingOn(first, second));

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("c@test.local", sent.To);
        Assert.Contains("Sân 2", sent.Body);
        _repository.Verify(x => x.DeleteOverlappingSlotInterestsAsync(second.Id, Date, Start, End), Times.Once);
        _repository.Verify(x => x.DeleteOverlappingSlotInterestsAsync(first.Id, Date, Start, End), Times.Never);
    }

    [Fact]
    public async Task OneFailedEmail_DoesNotStopTheOthersOrTheCleanup()
    {
        var court = Court("Sân 3");
        Interested(court.Id, "broken@test.local", "ok@test.local");
        _email.FailFor.Add("broken@test.local");

        await Create().NotifyReleasedSlotsAsync(BookingOn(court));

        Assert.Equal("ok@test.local", Assert.Single(_email.Sent).To);
        _repository.Verify(x => x.DeleteOverlappingSlotInterestsAsync(court.Id, Date, Start, End), Times.Once);
    }
}
