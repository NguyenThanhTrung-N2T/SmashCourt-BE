using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// T-15 trên schema thật (BE-IT-25, phần hàng việc của dashboard; plan 6A-6 Task 8a): mọi giờ trong
/// <c>ManagerDashboardActionItemDto</c> là giờ tường VN dạng <c>yyyy-MM-ddTHH:mm:ss</c> không offset (FE đọc theo giờ máy). Trước khi sửa
/// <c>createdAt</c> là giờ UTC cùng định dạng ⇒ FE hiện "Tạo lúc" sớm 7 giờ. Mỗi test một chi nhánh riêng.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-25")]
public sealed class ManagerDashboardActionQueueTimeTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Today = new(2020, 11, 20);

    private ReportRepository Repository => new(DbContext);

    private static DateTime Utc(int d, int h, int min, int s = 0) => new(2020, 11, d, h, min, s, DateTimeKind.Utc);

    private async Task<string?> CreatedAtInQueueAsync(BookingStatus status, DateTime createdAtUtc, DateOnly? bookingDate = null)
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, status, createdAtUtc);
        booking.BookingDate = bookingDate ?? Today;
        DbContext.Bookings.Add(booking);
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(
            booking.Id, seed.Court.Id, booking.BookingDate, new TimeOnly(18, 0), new TimeOnly(19, 30)));
        await DbContext.SaveChangesAsync();

        var queue = await Repository.GetManagerDashboardActionQueueAsync(seed.Branch.Id, Today);

        var item = Assert.Single(queue);
        Assert.Equal(booking.Id, item.BookingId);
        // Giờ chơi vốn là giờ tường VN: không đổi
        Assert.Equal($"{booking.BookingDate:yyyy-MM-dd}T18:00:00", item.StartTime);
        Assert.Equal($"{booking.BookingDate:yyyy-MM-dd}T19:30:00", item.EndTime);
        return item.CreatedAt;
    }

    [Theory]
    [InlineData(BookingStatus.PENDING_PAYMENT)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    public async Task CreatedAt_IsVietnamWallClock(BookingStatus status)
    {
        // 13:58:07 UTC = 20:58:07 giờ VN
        Assert.Equal("2020-11-20T20:58:07", await CreatedAtInQueueAsync(status, Utc(20, 13, 58, 7)));
    }

    [Fact]
    public async Task CreatedAt_JustAfterVietnamMidnight_IsOnTheVietnamDay()
    {
        // 17:30 UTC ngày 19 = 00:30 giờ VN ngày 20
        Assert.Equal("2020-11-20T00:30:00", await CreatedAtInQueueAsync(BookingStatus.PENDING_PAYMENT, Utc(19, 17, 30)));
    }

    [Fact]
    public async Task CreatedAt_LateEveningUtc_MovesToTheNextVietnamDay()
    {
        // 20:15 UTC ngày 19 = 03:15 giờ VN ngày 20; đơn chơi hôm trước vẫn nằm trong hàng việc (BookingDate <= hôm nay)
        Assert.Equal("2020-11-20T03:15:00",
            await CreatedAtInQueueAsync(BookingStatus.PENDING_PAYMENT, Utc(19, 20, 15), Today.AddDays(-1)));
    }
}
