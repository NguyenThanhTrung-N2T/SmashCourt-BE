using System.Globalization;
using System.Text.Json;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Đối chiếu dữ liệu dựng request (sân, giá trước giảm, tổng sau giảm, hạng, mốc thời gian) do BE đọc với truy vấn Phase 5
/// <c>export_smashcourt_bookings.sql</c> trên cùng dữ liệu. Chỉ chạy khi có thư mục TADSS-Runtime cạnh repo BE; trên CI bỏ qua.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-UT-02")]
public sealed class BookingDecisionSnapshotPhase5EquivalenceTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetDecisionSnapshotAsync_WhenComparedWithPhase5ExportSql_GivesSameItemsTotalsTierAndTimes()
    {
        var sqlPath = Phase5ExportSql.FindPath();
        if (sqlPath == null)
            return; // Không có TADSS-Runtime cạnh repo BE (CI)

        var seed = await Seeder.SeedBranchWithCourtAsync();
        var courts = new List<Court> { seed.Court };
        for (var i = 0; i < 2; i++)
            courts.Add(await Seeder.SeedCourtAsync(seed));
        var slots = new List<TimeSlot>();
        for (var hour = 6; hour < 12; hour++)
            slots.Add(await Seeder.SeedTimeSlotAsync(new TimeOnly(hour, 0), new TimeOnly(hour + 1, 0)));
        // Hạng 0 và 100 đã có sẵn (SeedBranchWithCourtAsync); thêm 3 hạng để có hạng ở nhiều thứ hạng khác nhau
        var tiers = new[] { await Seeder.SeedTierAsync(0), await Seeder.SeedTierAsync(100), await Seeder.SeedTierAsync(1_000), await Seeder.SeedTierAsync(5_000) };

        var rng = new Random(20261021);
        var bookingIds = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var customer = await Seeder.SeedCustomerAsync(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rng.Next(0, 500_000)));
            if (i % 4 != 0) // một phần khách chưa có hạng
                await Seeder.SeedCustomerLoyaltyAsync(customer, tiers[rng.Next(tiers.Length)]);

            var createdAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(rng.Next(0, 3_000_000)).AddTicks(rng.Next(0, 1_000_000) * 10L);
            var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, createdAt);
            DbContext.Bookings.Add(booking);
            var courtFee = 100_000m * rng.Next(1, 5);
            var invoice = TestDataSeeder.NewInvoice(booking, courtFee);
            invoice.PromotionDiscountAmount = rng.Next(0, 2) * 10_000m; // final_total khác tổng giá sân khi có giảm
            invoice.FinalTotal = courtFee - invoice.PromotionDiscountAmount;
            DbContext.Invoices.Add(invoice);
            await DbContext.SaveChangesAsync();

            // 1–3 dòng sân, ngày riêng cho mỗi booking để không đụng ràng buộc loại trừ chồng giờ; một số dòng không hoạt động
            var lineCount = rng.Next(1, 4);
            var allInactive = i % 5 == 4;
            for (var line = 0; line < lineCount; line++)
            {
                var slot = slots[rng.Next(slots.Count)];
                var date = new DateOnly(2026, 11, 1).AddDays(i * 3 + line);
                var active = !allInactive && (line == 0 || rng.Next(3) != 0);
                var bookingCourt = TestDataSeeder.NewBookingCourt(booking.Id, courts[rng.Next(courts.Count)].Id, date, slot.StartTime, slot.EndTime, active);
                DbContext.BookingCourts.Add(bookingCourt);
                await DbContext.SaveChangesAsync();
                var priceCount = rng.Next(0, 3);
                for (var p = 0; p < priceCount; p++)
                    DbContext.BookingPriceItems.Add(TestDataSeeder.NewPriceItem(bookingCourt.Id, slots[(slots.IndexOf(slot) + p) % slots.Count].Id, 40_000m + rng.Next(0, 5) * 5_000m));
                await DbContext.SaveChangesAsync();
            }

            bookingIds.Add(booking.Id);
        }

        var expectedRows = await Phase5ExportSql.RunAsync(DbContext, sqlPath, bookingIds.ToHashSet());
        var repository = new BookingRepository(DbContext);
        var problems = new List<string>();
        var checkedTiers = new HashSet<int?>();
        var checkedItemCounts = new HashSet<int>();
        foreach (var id in bookingIds)
        {
            var snapshot = await repository.GetDecisionSnapshotAsync(id);
            var row = expectedRows[id];
            if (snapshot == null)
            {
                problems.Add($"{id}: BE trả null");
                continue;
            }

            if ((decimal)row["final_total"]! != snapshot.FinalTotal)
                problems.Add($"{id}: final_total SQL={row["final_total"]} BE={snapshot.FinalTotal}");
            var expectedRank = row["tier_rank"] == null ? (int?)null : (int)(long)row["tier_rank"]!;
            if (expectedRank != snapshot.TierRank)
                problems.Add($"{id}: tier_rank SQL={expectedRank} BE={snapshot.TierRank}");
            checkedTiers.Add(expectedRank);
            if (ParseUtc((string)row["created_at"]!) != snapshot.CreatedAt)
                problems.Add($"{id}: created_at SQL={row["created_at"]} BE={snapshot.CreatedAt:O}");
            if (ParseUtc((string)row["member_since"]!) != snapshot.CustomerCreatedAt)
                problems.Add($"{id}: member_since SQL={row["member_since"]} BE={snapshot.CustomerCreatedAt:O}");

            var items = JsonDocument.Parse((string)row["items"]!).RootElement.EnumerateArray().ToList();
            checkedItemCounts.Add(items.Count);
            if (items.Count != snapshot.Courts.Count)
            {
                problems.Add($"{id}: số sân SQL={items.Count} BE={snapshot.Courts.Count}");
                continue;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var court = snapshot.Courts[i];
                var same = item.GetProperty("court_id").GetGuid() == court.CourtId
                           && item.GetProperty("court_type_id").GetGuid() == court.CourtTypeId
                           && DateOnly.ParseExact(item.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture) == court.Date
                           && TimeOnly.Parse(item.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture) == court.StartTime
                           && TimeOnly.Parse(item.GetProperty("end_time").GetString()!, CultureInfo.InvariantCulture) == court.EndTime
                           && item.GetProperty("price").GetDecimal() == court.Price;
                if (!same)
                    problems.Add($"{id}: sân thứ {i} lệch: SQL={item} BE={court}");
            }
        }

        Assert.True(problems.Count == 0, $"{problems.Count} chỗ lệch:\n{string.Join("\n", problems.Take(10))}");
        // Dữ liệu đủ đa dạng: có khách chưa hạng, nhiều thứ hạng khác nhau, 1…3 sân
        Assert.Contains(null, checkedTiers);
        Assert.True(checkedTiers.Count(t => t != null) >= 3, $"Chưa đủ thứ hạng: {string.Join(",", checkedTiers)}");
        Assert.True(checkedItemCounts.Count >= 2, $"Chưa đủ biến thể số sân: {string.Join(",", checkedItemCounts)}");
    }

    // SQL xuất ISO 8601 UTC micro giây: 2026-10-01T00:00:00.123456Z
    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
