using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Chọn đơn cần báo outcome (gói 6A-5 Task 8, C2/D1 — spec 7.11, P6-19) trên schema thật: đơn có quyết định TADSS, chưa báo, đã kết thúc
/// (ba trạng thái hủy, hoặc COMPLETED / NO_SHOW / PENDING_PAYMENT đã qua giờ kết thúc theo giờ VN); <c>updated_at</c> mới nhất trước để đơn vừa
/// kết thúc không bị kẹt sau các đơn cũ bị từ chối vĩnh viễn. Đánh dấu "đã báo" có điều kiện (lần chạy chồng không ghi đè mốc đầu).
/// Giờ chơi của đơn mẫu: ngày <see cref="BookingDepositTestBase.PlayDate"/>, 18:00–19:00.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OutcomeCandidateRepositoryTests(PostgreSqlIntegrationFixture fixture) : BookingDepositTestBase(fixture)
{
    private static readonly TimeOnly AfterPlay = new(20, 0);

    private readonly List<Guid> _mine = [];

    private async Task<PendingOrder> SeedAsync(BookingStatus status, bool withDecision = true, DateTime? updatedAt = null, DateOnly? playDate = null)
    {
        var order = await SeedOrderAsync(status, playDate: playDate);
        order.Booking.TadssDecisionId = withDecision ? order.Booking.TadssDecisionId ?? Guid.NewGuid() : null;
        order.Booking.UpdatedAt = updatedAt ?? CreatedAt;
        await DbContext.SaveChangesAsync();
        _mine.Add(order.Booking.Id);
        return order;
    }

    private async Task<List<Guid>> CandidatesAsync(DateOnly today, TimeOnly nowTime, int take = 100) =>
        (await Repository.GetOutcomeCandidatesAsync(today, nowTime, take)).Select(b => b.Id).Where(_mine.Contains).ToList();

    // ───────────────────────── Đơn được chọn ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.COMPLETED)]
    [InlineData(BookingStatus.NO_SHOW)]
    [InlineData(BookingStatus.PENDING_PAYMENT)]
    public async Task Candidates_PlayedOrderAfterItsEnd_AreTaken(BookingStatus status)
    {
        var playedToday = await SeedAsync(status);
        var playedYesterday = await SeedAsync(status, playDate: PlayDate.AddDays(-1));

        var ids = await CandidatesAsync(PlayDate, AfterPlay);

        Assert.Contains(playedToday.Booking.Id, ids);
        Assert.Contains(playedYesterday.Booking.Id, ids);
    }

    [Theory]
    [InlineData(18, 59, false)]
    [InlineData(19, 0, true)]   // giờ kết thúc ≤ bây giờ
    public async Task Candidates_PlayedToday_OnlyOnceTheEndHasPassed(int hour, int minute, bool taken)
    {
        var order = await SeedAsync(BookingStatus.COMPLETED);

        var ids = await CandidatesAsync(PlayDate, new TimeOnly(hour, minute));

        Assert.Equal(taken, ids.Contains(order.Booking.Id));
    }

    [Fact]
    public async Task Candidates_PlayedOrderOnALaterDay_IsNotTaken()
    {
        // Check-out sớm một đơn ngày mai: chưa tới giờ kết thúc
        var order = await SeedAsync(BookingStatus.COMPLETED, playDate: PlayDate.AddDays(1));

        Assert.DoesNotContain(order.Booking.Id, await CandidatesAsync(PlayDate, AfterPlay));
    }

    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.CANCELLED_REFUNDED)]
    public async Task Candidates_CancelledOrders_AreTakenWhateverThePlayTime(BookingStatus status)
    {
        var order = await SeedAsync(status, playDate: PlayDate.AddDays(5));

        Assert.Contains(order.Booking.Id, await CandidatesAsync(PlayDate, AfterPlay));
    }

    [Fact]
    public async Task Candidates_IncludeTheCourtRows()
    {
        var order = await SeedAsync(BookingStatus.NO_SHOW);

        var candidate = Assert.Single(await Repository.GetOutcomeCandidatesAsync(PlayDate, AfterPlay, 100), b => b.Id == order.Booking.Id);

        var court = Assert.Single(candidate.BookingCourts);
        Assert.Equal(End, court.EndTime);
    }

    // ───────────────────────── Đơn bị bỏ ─────────────────────────

    [Theory]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.IN_PROGRESS)]
    public async Task Candidates_OrderNotFinished_IsNotTaken(BookingStatus status)
    {
        var order = await SeedAsync(status, playDate: PlayDate.AddDays(-1));

        Assert.DoesNotContain(order.Booking.Id, await CandidatesAsync(PlayDate, AfterPlay));
    }

    [Fact]
    public async Task Candidates_OrderWithoutTadssDecision_IsNotTaken()
    {
        // Đơn tạo khi tắt cờ / TADSS lỗi (cọc 100 % dự phòng) không có quyết định để báo
        var order = await SeedAsync(BookingStatus.COMPLETED, withDecision: false);

        Assert.DoesNotContain(order.Booking.Id, await CandidatesAsync(PlayDate, AfterPlay));
    }

    [Fact]
    public async Task Candidates_AlreadyReported_IsNotTaken()
    {
        var order = await SeedAsync(BookingStatus.CANCELLED);
        Assert.True(await Repository.MarkOutcomeReportedAsync(order.Booking.Id, Now));

        Assert.DoesNotContain(order.Booking.Id, await CandidatesAsync(PlayDate, AfterPlay));
    }

    // ───────────────────────── Thứ tự và giới hạn (D1) ─────────────────────────

    [Fact]
    public async Task Candidates_NewestUpdatedFirst()
    {
        var oldest = await SeedAsync(BookingStatus.CANCELLED, updatedAt: CreatedAt.AddHours(-3));
        var newest = await SeedAsync(BookingStatus.NO_SHOW, updatedAt: CreatedAt.AddHours(2));
        var middle = await SeedAsync(BookingStatus.COMPLETED, updatedAt: CreatedAt);

        Assert.Equal([newest.Booking.Id, middle.Booking.Id, oldest.Booking.Id], await CandidatesAsync(PlayDate, AfterPlay));
    }

    [Fact]
    public async Task Candidates_RespectsTakeAndKeepsTheNewest()
    {
        // Đơn mẫu đặt updated_at ở tương lai xa để luôn mới nhất so với dữ liệu khác trong DB dùng chung
        var far = DateTime.UtcNow.AddYears(5);
        await SeedAsync(BookingStatus.CANCELLED, updatedAt: far);
        var second = await SeedAsync(BookingStatus.CANCELLED, updatedAt: far.AddMinutes(1));
        var first = await SeedAsync(BookingStatus.CANCELLED, updatedAt: far.AddMinutes(2));

        var ids = (await Repository.GetOutcomeCandidatesAsync(PlayDate, AfterPlay, 2)).Select(b => b.Id).ToList();

        Assert.Equal([first.Booking.Id, second.Booking.Id], ids);
    }

    [Fact]
    public async Task Candidates_RejectsNonPositiveTake()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Repository.GetOutcomeCandidatesAsync(PlayDate, AfterPlay, 0));
    }

    // ───────────────────────── Đánh dấu đã báo ─────────────────────────

    [Fact]
    public async Task MarkOutcomeReported_SecondTime_ReturnsFalseAndKeepsTheFirstTimestamp()
    {
        var order = await SeedAsync(BookingStatus.COMPLETED);

        Assert.True(await Repository.MarkOutcomeReportedAsync(order.Booking.Id, Now));
        Assert.False(await Repository.MarkOutcomeReportedAsync(order.Booking.Id, Now.AddMinutes(5)));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(Now, booking.TadssOutcomeReportedAt);
    }

    [Fact]
    public async Task MarkOutcomeReported_DoesNotTouchStatusOrUpdatedAt()
    {
        // updated_at là thứ tự chọn đơn; đánh dấu đã báo không phải một thay đổi nghiệp vụ
        var order = await SeedAsync(BookingStatus.NO_SHOW, updatedAt: CreatedAt.AddMinutes(-7));

        Assert.True(await Repository.MarkOutcomeReportedAsync(order.Booking.Id, Now));

        var booking = await ReloadBookingAsync(order.Booking.Id);
        Assert.Equal(BookingStatus.NO_SHOW, booking.Status);
        Assert.Equal(CreatedAt.AddMinutes(-7), booking.UpdatedAt);
    }

    [Fact]
    public async Task MarkOutcomeReported_UnknownBooking_ReturnsFalse()
    {
        Assert.False(await Repository.MarkOutcomeReportedAsync(Guid.NewGuid(), Now));
    }

    [Fact]
    public async Task MarkOutcomeReported_RejectsNonUtcClock()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Repository.MarkOutcomeReportedAsync(Guid.NewGuid(), DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    // Đơn dùng chung DB: kiểm có đúng một bản ghi của đơn mẫu bị đổi
    [Fact]
    public async Task MarkOutcomeReported_ChangesOnlyThatBooking()
    {
        var target = await SeedAsync(BookingStatus.COMPLETED);
        var other = await SeedAsync(BookingStatus.COMPLETED);

        await Repository.MarkOutcomeReportedAsync(target.Booking.Id, Now);

        Assert.Null((await DbContext.Bookings.AsNoTracking().SingleAsync(b => b.Id == other.Booking.Id)).TadssOutcomeReportedAt);
    }
}
