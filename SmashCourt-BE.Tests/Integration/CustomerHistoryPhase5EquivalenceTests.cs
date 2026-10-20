using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// Đối chiếu lịch sử khách do BE tính (repository + calculator) với truy vấn Phase 5
/// <c>export_smashcourt_bookings.sql</c> trên cùng dữ liệu: mỗi booking của khách là một "điểm kiểm" với
/// <c>created_at</c> riêng, nên mọi quy tắc thời gian (so nghiêm ngặt, đổi giờ VN sang UTC, ranh giới nửa đêm)
/// đều bị so với bản gốc. Chỉ chạy khi có thư mục TADSS-Runtime cạnh repo BE (máy dev); trên CI bỏ qua.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-UT-09")]
public sealed class CustomerHistoryPhase5EquivalenceTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private const int BookingCount = 60;
    private static readonly DateOnly FirstPlayDate = new(2026, 1, 1);

    private sealed record Probe(Guid Id, DateTime CreatedAt);

    [Fact]
    public async Task Calculate_WhenComparedWithPhase5ExportSql_GivesSameCountsAtEveryBooking()
    {
        var sqlPath = Phase5ExportSql.FindPath();
        if (sqlPath == null)
            return; // Không có TADSS-Runtime cạnh repo BE (CI)

        var customer = await Seeder.SeedCustomerAsync();
        var probes = await SeedMixedHistoryAsync(customer, new Random(20261020));

        var expectedRows = await Phase5ExportSql.RunAsync(DbContext, sqlPath, probes.Select(p => p.Id).ToHashSet());
        var historyColumns = new[] { "history_completed", "history_no_show", "history_cancelled", "history_late_cancelled", "history_payment_expired" };
        var expected = expectedRows.ToDictionary(row => row.Key, row => historyColumns.Select(c => (int)(long)row.Value[c]!).ToArray());

        var repository = new CustomerHistoryRepository(DbContext);
        var mismatches = new List<string>();
        var totals = new int[5];
        foreach (var probe in probes)
        {
            var entries = await repository.GetEntriesAsync(customer.Id, probe.Id);
            var actual = CustomerHistoryCalculator.Calculate(entries, probe.CreatedAt);
            var actualCounts = new[]
            {
                actual.CompletedCount, actual.NoShowCount, actual.CancelledCount, actual.LateCancelledCount, actual.PaymentExpiredCount
            };
            for (var i = 0; i < totals.Length; i++)
                totals[i] += actualCounts[i];

            var expectedCounts = expected[probe.Id];
            if (!expectedCounts.SequenceEqual(actualCounts))
                mismatches.Add($"{probe.CreatedAt:O}: SQL=[{string.Join(",", expectedCounts)}] BE=[{string.Join(",", actualCounts)}]");
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count}/{probes.Count} điểm kiểm lệch (completed, no_show, cancelled, late, payment_expired):\n{string.Join("\n", mismatches.Take(10))}");
        // Dữ liệu phải đủ đa dạng để phép so sánh có ý nghĩa: mọi ô đều có giá trị khác 0 ở ít nhất một điểm kiểm
        Assert.All(totals, total => Assert.True(total > 0, $"Một ô chưa được phủ: [{string.Join(",", totals)}]"));
    }

    // Dựng lịch sử hỗn hợp; mốc created_at/cancelled_at của booking sau được đặt sát (trùng, ±1 µs, ±1 phút, ±1 giờ) mốc kết thúc của booking trước
    private async Task<List<Probe>> SeedMixedHistoryAsync(User customer, Random rng)
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var anchors = new List<DateTime>(); // mốc kết thúc chơi và mốc hủy của các booking đã dựng
        TimeSpan[] deltas =
        [
            TimeSpan.Zero, TimeSpan.FromTicks(-10), TimeSpan.FromTicks(10), TimeSpan.FromMinutes(-1),
            TimeSpan.FromMinutes(1), TimeSpan.FromHours(-1), TimeSpan.FromHours(1), TimeSpan.FromHours(10)
        ];
        var probes = new List<Probe>();

        for (var i = 0; i < BookingCount; i++)
        {
            var playDate = FirstPlayDate.AddDays(i);
            var playEnd = new TimeOnly(rng.Next(8, 23), rng.Next(2) * 30);
            var playEndUtc = DateTimeHelper.ToUtcFromVietnam(playDate, playEnd);
            var createdAt = anchors.Count == 0 || rng.Next(5) == 0
                ? playEndUtc.AddMinutes(rng.Next(-3000, 3000))
                : anchors[rng.Next(anchors.Count)] + deltas[rng.Next(deltas.Length)];

            var kind = rng.Next(9);
            var status = kind switch
            {
                0 or 7 => BookingStatus.COMPLETED,
                1 => BookingStatus.NO_SHOW,
                2 or 3 or 4 or 5 => new[] { BookingStatus.CANCELLED, BookingStatus.CANCELLED_PENDING_REFUND, BookingStatus.CANCELLED_REFUNDED }[rng.Next(3)],
                _ => BookingStatus.PENDING
            };

            var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, status, createdAt);
            var cancelledAt = createdAt + new[] { TimeSpan.Zero, TimeSpan.FromTicks(10), TimeSpan.FromMinutes(5), TimeSpan.FromHours(3), TimeSpan.FromDays(2) }[rng.Next(5)];
            switch (kind)
            {
                case 2: // hết hạn thanh toán (Phase 5: cancel_source SYSTEM)
                    booking.CancelSource = CancelSourceEnum.SYSTEM;
                    booking.CancelReason = BookingCancelReason.PAYMENT_EXPIRED;
                    break;
                case 3 or 4: // khách tự hủy: chưa trả (kind 3) hoặc đã trả với tỷ lệ hoàn khác nhau (kind 4)
                    booking.CancelSource = rng.Next(2) == 0 ? CancelSourceEnum.CUSTOMER : CancelSourceEnum.LINK;
                    booking.CancelReason = BookingCancelReason.CUSTOMER_CANCELLED;
                    break;
                case 5: // nhân viên hủy: không tính
                    booking.CancelSource = CancelSourceEnum.STAFF;
                    booking.CancelReason = BookingCancelReason.STAFF_CANCELLED;
                    break;
            }

            if (kind is >= 2 and <= 5)
                booking.CancelledAt = cancelledAt;
            DbContext.Bookings.Add(booking);
            if (kind != 7 && kind != 8) // kind 7: hoàn tất nhưng không có dòng sân; kind 8: đơn đang mở
                DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(
                    booking.Id, seed.Court.Id, playDate, playEnd.AddMinutes(-60), playEnd));
            await DbContext.SaveChangesAsync();

            if (kind == 3 && rng.Next(2) == 0)
                await Seeder.SeedPaymentAsync(booking, PaymentTxStatus.PENDING);
            if (kind == 4)
            {
                var refunds = new[] { Array.Empty<decimal>(), [0m], [50m], [99.99m], [100m], [30m, 100m] }[rng.Next(6)];
                await Seeder.SeedPaymentAsync(booking, rng.Next(2) == 0 ? PaymentTxStatus.SUCCESS : PaymentTxStatus.REFUNDED, refunds);
            }

            if (kind is 0 or 1)
                anchors.Add(playEndUtc);
            if (kind is >= 2 and <= 5)
                anchors.Add(cancelledAt);
            probes.Add(new Probe(booking.Id, createdAt));
        }

        return probes;
    }
}
