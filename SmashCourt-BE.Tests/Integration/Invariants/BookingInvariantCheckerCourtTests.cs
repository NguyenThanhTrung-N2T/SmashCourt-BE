using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// BE-IT-20, SC6-4 (plan 6A-6 Task 9, 9 ca): SC6-4a đơn hiệu lực phải bật mọi dòng sân; SC6-4b không có hai đơn cùng giữ một sân chồng giờ.
/// "Đang giữ sân" = đơn hiệu lực, không lọc <c>is_active</c> (để bắt hậu quả của dòng bị tắt sai, vì <c>excl_booking_courts_no_overlap</c>
/// chỉ áp dòng đang bật), trừ dòng đã tắt của đơn <c>PENDING_PAYMENT</c> (Job-02 nhả sân, P6-25). Chồng giờ theo nửa khoảng.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-20")]
public sealed class BookingInvariantCheckerCourtTests(PostgreSqlIntegrationFixture fixture) : InvariantTestBase(fixture)
{
    // (1)
    [Fact]
    public async Task TwoActiveOrdersWithoutOverlap_NoViolation()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var a = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var b = await SeedLineAsync(seed, seed.Court, BookingStatus.PAID_ONLINE, "09:30", "10:30");

        AssertNone(await CheckAsync(a.Id, b.Id));
    }

    // (2) DB cho qua vì dòng của b đã bị tắt sai
    [Fact]
    public async Task OverlapThroughAWronglyDisabledLine_ReportsBothRules()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var a = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var b = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:30", "09:30", isActive: false);

        var violations = await CheckAsync(a.Id, b.Id);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Rule == InvariantRules.ActiveCourtsAreActive && v.BookingId == b.Id);
        var pair = Assert.Single(violations, v => v.Rule == InvariantRules.NoDoubleBooking);
        Assert.Equal(a.Id.CompareTo(b.Id) < 0 ? a.Id : b.Id, pair.BookingId); // cả hai trong phạm vi: báo một lần
        Assert.Contains(a.Id.ToString(), pair.Detail);
        Assert.Contains(b.Id.ToString(), pair.Detail);
    }

    // (3)
    [Fact]
    public async Task SameTimeOnDifferentCourts_NoViolation()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var other = await Seeder.SeedCourtAsync(seed);
        var a = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var b = await SeedLineAsync(seed, other, BookingStatus.CONFIRMED, "08:00", "09:00");

        AssertNone(await CheckAsync(a.Id, b.Id));
    }

    // (4)
    [Theory]
    [InlineData(BookingStatus.CANCELLED)]
    [InlineData(BookingStatus.CANCELLED_PENDING_REFUND)]
    [InlineData(BookingStatus.COMPLETED)]
    public async Task NonHoldingOrderOverlappingAnActiveOne_NoViolation(BookingStatus status)
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var a = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var b = await SeedLineAsync(seed, seed.Court, status, "08:00", "09:00", isActive: false);

        AssertNone(await CheckAsync(a.Id, b.Id));
    }

    // (5)
    [Theory]
    [InlineData(BookingStatus.PAID_ONLINE)]
    [InlineData(BookingStatus.CONFIRMED)]
    [InlineData(BookingStatus.IN_PROGRESS)]
    [InlineData(BookingStatus.PENDING)]
    [InlineData(BookingStatus.PENDING_APPROVAL)]
    public async Task ActiveOrderWithADisabledLine_NoOverlap_OnlySc64a(BookingStatus status)
    {
        var order = await SeedOrderAsync(status);
        foreach (var line in DbContext.BookingCourts.Where(c => c.BookingId == order.Booking.Id))
            line.IsActive = false;
        await DbContext.SaveChangesAsync();

        AssertOnly(await CheckAsync(order.Booking.Id), InvariantRules.ActiveCourtsAreActive, order.Booking.Id);
    }

    // (6) Job-02 nhả sân của đơn chưa thanh toán đủ khi hết giờ chơi: đúng thiết kế
    [Fact]
    public async Task PendingPaymentWithReleasedLineOverlappingAConfirmedOrder_NoViolation()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var released = await SeedLineAsync(seed, seed.Court, BookingStatus.PENDING_PAYMENT, "08:00", "09:00", isActive: false);
        var next = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:30", "09:30");

        AssertNone(await CheckAsync(released.Id, next.Id));
    }

    // Đơn PENDING_PAYMENT còn bật dòng sân vẫn giữ sân: chồng giờ qua một dòng bị tắt sai của đơn khác vẫn bị bắt
    [Fact]
    public async Task PendingPaymentStillHoldingOverlappedByAWronglyDisabledLine_Sc64b()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var holding = await SeedLineAsync(seed, seed.Court, BookingStatus.PENDING_PAYMENT, "08:00", "09:00");
        var wrong = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:30", "09:30", isActive: false);

        var violations = await CheckAsync(holding.Id);

        AssertOnly(violations, InvariantRules.NoDoubleBooking, holding.Id);
    }

    // (7) nửa khoảng: kết thúc 09:00 và bắt đầu 09:00 không chồng; chồng 1 phút thì có
    [Theory]
    [InlineData("09:00", "10:00", 0)]
    [InlineData("08:59", "10:00", 1)]
    [InlineData("07:00", "08:00", 0)]
    [InlineData("07:00", "08:01", 1)]
    [InlineData("08:15", "08:45", 1)] // nằm trọn bên trong
    public async Task HalfOpenIntervals(string start, string end, int expectedPairs)
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var a = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var b = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, start, end, isActive: false);

        var violations = await CheckAsync(a.Id);

        Assert.Equal(expectedPairs, violations.Count(v => v.Rule == InvariantRules.NoDoubleBooking));
        Assert.All(violations, v => Assert.Equal(a.Id, v.BookingId)); // b ngoài phạm vi: không báo SC6-4a của b
    }

    // (8)
    [Fact]
    public async Task SameHoursOnConsecutiveDays_NoViolation()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var a = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "23:00", "23:59", date: PlayDate);
        var b = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "23:00", "23:59", isActive: false, date: PlayDate.AddDays(1));

        Assert.DoesNotContain(await CheckAsync(a.Id, b.Id), v => v.Rule == InvariantRules.NoDoubleBooking);
    }

    // (9)
    [Fact]
    public async Task PairWithOneOrderOutOfScope_IsReportedForTheOrderInScope()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var inScope = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00", isActive: false);
        var outOfScope = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");

        var violations = await CheckAsync(inScope.Id);

        var pair = Assert.Single(violations, v => v.Rule == InvariantRules.NoDoubleBooking);
        Assert.Equal(inScope.Id, pair.BookingId);
        Assert.Contains(outOfScope.Id.ToString(), pair.Detail);
    }

    [Fact]
    public async Task PairWithBothOrdersOutOfScope_IsNotReported()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00", isActive: false);
        await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var unrelated = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "12:00", "13:00");

        AssertNone(await CheckAsync(unrelated.Id));
    }

    [Fact]
    public async Task ThreeOrdersOnOneSlot_EachPairReportedOnce()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var a = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var b = await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00", isActive: false);
        var c = await SeedLineAsync(seed, seed.Court, BookingStatus.PAID_ONLINE, "08:00", "09:00", isActive: false);

        var violations = await CheckAsync(a.Id, b.Id, c.Id);

        Assert.Equal(3, violations.Count(v => v.Rule == InvariantRules.NoDoubleBooking));
        Assert.Equal(2, violations.Count(v => v.Rule == InvariantRules.ActiveCourtsAreActive));
    }
}
