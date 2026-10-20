using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>BE-UT-02 (phần truy vấn): repository đọc đủ và đúng dữ liệu để dựng request quyết định cọc.</summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-UT-02")]
public sealed class BookingDecisionSnapshotTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly PlayDate = new(2026, 11, 20);

    private BookingRepository Repository => new(DbContext);

    private async Task<(Booking Booking, User Customer, BranchSeedData Seed)> SeedBookingWithInvoiceAsync(
        DateTime? customerCreatedAt = null, DateTime? bookingCreatedAt = null)
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync(customerCreatedAt);
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, bookingCreatedAt);
        DbContext.Bookings.Add(booking);
        var invoice = TestDataSeeder.NewInvoice(booking, courtFee: 190_000m);
        invoice.LoyaltyDiscountAmount = 10_000m;
        invoice.FinalTotal = 180_000m; // chk_invoices_final_total_formula
        DbContext.Invoices.Add(invoice);
        await DbContext.SaveChangesAsync();
        return (booking, customer, seed);
    }

    private async Task<BookingCourt> AddCourtAsync(
        Booking booking, Court court, DateOnly date, TimeOnly start, TimeOnly end, bool isActive = true, params (TimeSlot Slot, decimal Price)[] prices)
    {
        var bookingCourt = TestDataSeeder.NewBookingCourt(booking.Id, court.Id, date, start, end, isActive);
        DbContext.BookingCourts.Add(bookingCourt);
        await DbContext.SaveChangesAsync();
        foreach (var (slot, price) in prices)
            DbContext.BookingPriceItems.Add(TestDataSeeder.NewPriceItem(bookingCourt.Id, slot.Id, price));
        await DbContext.SaveChangesAsync();
        return bookingCourt;
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenBookingHasTwoCourts_ReturnsOneItemPerCourtWithPriceBeforeDiscount()
    {
        var (booking, customer, seed) = await SeedBookingWithInvoiceAsync();
        var second = await Seeder.SeedCourtAsync(seed);
        var slot1 = await Seeder.SeedTimeSlotAsync(new TimeOnly(18, 0), new TimeOnly(19, 0));
        var slot2 = await Seeder.SeedTimeSlotAsync(new TimeOnly(19, 0), new TimeOnly(20, 0));
        await AddCourtAsync(booking, seed.Court, PlayDate, new TimeOnly(18, 0), new TimeOnly(20, 0), true, (slot1, 50_000m), (slot2, 60_000m));
        await AddCourtAsync(booking, second, PlayDate, new TimeOnly(18, 0), new TimeOnly(20, 0), true, (slot1, 40_000m), (slot2, 40_000m));

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Courts.Count);
        Assert.Equal(110_000m, snapshot.Courts.Single(c => c.CourtId == seed.Court.Id).Price);
        Assert.Equal(80_000m, snapshot.Courts.Single(c => c.CourtId == second.Id).Price);
        Assert.All(snapshot.Courts, c => Assert.Equal(seed.CourtType.Id, c.CourtTypeId));
        Assert.Equal(booking.Id, snapshot.BookingId);
        Assert.Equal(seed.Branch.Id, snapshot.BranchId);
        Assert.Equal(customer.Id, snapshot.CustomerId);
        // totalPrice là số sau giảm (final_total), khác tổng giá các sân trước giảm
        Assert.Equal(180_000m, snapshot.FinalTotal);
        Assert.Equal(190_000m, snapshot.Courts.Sum(c => c.Price));
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenCourtsAcrossDays_OrdersByDateStartTimeThenCourtForDeterministicRequest()
    {
        var (booking, _, seed) = await SeedBookingWithInvoiceAsync();
        var sameTime = new List<Court> { seed.Court };
        for (var i = 0; i < 3; i++)
            sameTime.Add(await Seeder.SeedCourtAsync(seed));
        // Chèn theo thứ tự sân GIẢM dần để thứ tự chèn (cũng là thứ tự đọc mặc định) khác thứ tự mong đợi
        var byIdDescending = sameTime.OrderByDescending(c => c.Id).ToList();
        await AddCourtAsync(booking, seed.Court, PlayDate.AddDays(1), new TimeOnly(8, 0), new TimeOnly(9, 0));
        foreach (var court in byIdDescending)
            await AddCourtAsync(booking, court, PlayDate, new TimeOnly(18, 0), new TimeOnly(19, 0));
        await AddCourtAsync(booking, seed.Court, PlayDate, new TimeOnly(6, 0), new TimeOnly(7, 0));

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        var expected = new List<(DateOnly, TimeOnly, Guid)> { (PlayDate, new TimeOnly(6, 0), seed.Court.Id) };
        expected.AddRange(sameTime.OrderBy(c => c.Id).Select(c => (PlayDate, new TimeOnly(18, 0), c.Id)));
        expected.Add((PlayDate.AddDays(1), new TimeOnly(8, 0), seed.Court.Id));
        Assert.Equal(expected, snapshot!.Courts.Select(c => (c.Date, c.StartTime, c.CourtId)));
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenCourtHasNoPriceItems_ReturnsZeroPrice()
    {
        var (booking, _, seed) = await SeedBookingWithInvoiceAsync();
        await AddCourtAsync(booking, seed.Court, PlayDate, new TimeOnly(18, 0), new TimeOnly(19, 0));

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.Equal(0m, Assert.Single(snapshot!.Courts).Price);
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenSomeCourtsAreInactive_ReturnsOnlyActiveOnes()
    {
        var (booking, _, seed) = await SeedBookingWithInvoiceAsync();
        var released = await Seeder.SeedCourtAsync(seed);
        await AddCourtAsync(booking, seed.Court, PlayDate, new TimeOnly(18, 0), new TimeOnly(19, 0));
        await AddCourtAsync(booking, released, PlayDate, new TimeOnly(18, 0), new TimeOnly(19, 0), isActive: false);

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.Equal(seed.Court.Id, Assert.Single(snapshot!.Courts).CourtId);
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenNoCourtIsActive_ReturnsAllCourts()
    {
        var (booking, _, seed) = await SeedBookingWithInvoiceAsync();
        var other = await Seeder.SeedCourtAsync(seed);
        await AddCourtAsync(booking, seed.Court, PlayDate, new TimeOnly(18, 0), new TimeOnly(19, 0), isActive: false);
        await AddCourtAsync(booking, other, PlayDate, new TimeOnly(18, 0), new TimeOnly(19, 0), isActive: false);

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.Equal(2, snapshot!.Courts.Count);
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenTimesAreStored_ReturnsUtcMomentsAndWallClockTimes()
    {
        var customerCreatedAt = new DateTime(2025, 3, 14, 1, 0, 0, DateTimeKind.Utc);
        var bookingCreatedAt = new DateTime(2026, 10, 19, 16, 55, 0, DateTimeKind.Utc); // 23:55 giờ VN
        var (booking, _, seed) = await SeedBookingWithInvoiceAsync(customerCreatedAt, bookingCreatedAt);
        await AddCourtAsync(booking, seed.Court, PlayDate, new TimeOnly(0, 15), new TimeOnly(1, 15));

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.Equal(bookingCreatedAt, snapshot!.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, snapshot.CreatedAt.Kind);
        Assert.Equal(customerCreatedAt, snapshot.CustomerCreatedAt);
        Assert.Equal(DateTimeKind.Utc, snapshot.CustomerCreatedAt.Kind);
        var court = Assert.Single(snapshot.Courts);
        Assert.Equal(PlayDate, court.Date);
        Assert.Equal(new TimeOnly(0, 15), court.StartTime); // giờ tường, không bị đổi múi giờ
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenPromotionHasCode_ReturnsCodeSnapshot()
    {
        var (booking, _, _) = await SeedBookingWithInvoiceAsync();
        var promotion = await Seeder.SeedPromotionAsync("WINTER");
        DbContext.BookingPromotions.Add(TestDataSeeder.NewBookingPromotion(booking.Id, promotion));
        await DbContext.SaveChangesAsync();

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.Equal("WINTER", snapshot!.PromotionCode);
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenPromotionHasNoCodeOrNoPromotion_ReturnsNullCode()
    {
        var (withUncodedPromotion, _, _) = await SeedBookingWithInvoiceAsync();
        var promotion = await Seeder.SeedPromotionAsync(null);
        DbContext.BookingPromotions.Add(TestDataSeeder.NewBookingPromotion(withUncodedPromotion.Id, promotion));
        await DbContext.SaveChangesAsync();
        var (withoutPromotion, _, _) = await SeedBookingWithInvoiceAsync();

        Assert.Null((await Repository.GetDecisionSnapshotAsync(withUncodedPromotion.Id))!.PromotionCode);
        Assert.Null((await Repository.GetDecisionSnapshotAsync(withoutPromotion.Id))!.PromotionCode);
    }

    // ── Hạng thành viên: thứ hạng theo min_points tăng dần, không theo tên ───────────

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenCustomerHasNoTier_ReturnsNullRank()
    {
        var (booking, _, _) = await SeedBookingWithInvoiceAsync();
        await Seeder.SeedTierAsync(0);

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.Null(snapshot!.TierRank);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(100, 2)]
    [InlineData(500, 3)]
    [InlineData(2_000, 4)]
    [InlineData(10_000, 5)]
    [InlineData(50_000, 6)]
    [InlineData(100_000, 7)]
    public async Task GetDecisionSnapshotAsync_WhenCustomerHasTier_ReturnsRankAmongAllTiersByMinPoints(int customerTierMinPoints, int expectedRank)
    {
        var (booking, customer, _) = await SeedBookingWithInvoiceAsync();
        LoyaltyTier? customerTier = null;
        // Tạo theo thứ tự lộn xộn để chắc chắn thứ hạng không phụ thuộc thứ tự tạo hay tên
        foreach (var minPoints in new[] { 10_000, 0, 100_000, 500, 50_000, 100, 2_000 })
        {
            var tier = await Seeder.SeedTierAsync(minPoints);
            if (minPoints == customerTierMinPoints)
                customerTier = tier;
        }

        await Seeder.SeedCustomerLoyaltyAsync(customer, customerTier!);

        var snapshot = await Repository.GetDecisionSnapshotAsync(booking.Id);

        Assert.Equal(expectedRank, snapshot!.TierRank);
    }

    // ── Không đủ dữ liệu dựng request ────────────────────────────────────────────

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenBookingDoesNotExist_ReturnsNull()
    {
        Assert.Null(await Repository.GetDecisionSnapshotAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenBookingHasNoInvoice_ReturnsNull()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id);
        DbContext.Bookings.Add(booking);
        await DbContext.SaveChangesAsync();

        Assert.Null(await Repository.GetDecisionSnapshotAsync(booking.Id));
    }

    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenBookingHasNoCustomer_ReturnsNull()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, Guid.Empty);
        booking.CustomerId = null;
        booking.GuestName = "Khách vãng lai";
        DbContext.Bookings.Add(booking);
        DbContext.Invoices.Add(TestDataSeeder.NewInvoice(booking));
        await DbContext.SaveChangesAsync();

        Assert.Null(await Repository.GetDecisionSnapshotAsync(booking.Id));
    }
}
