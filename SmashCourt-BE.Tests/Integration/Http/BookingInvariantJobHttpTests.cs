using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmashCourt_BE.Jobs;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Invariants;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Job kiểm tính đúng định kỳ trên host thật (DI của <c>Program.cs</c>, PostgreSQL thật): đơn bị làm hỏng có chủ đích và đã yên quá 2 phút thì job
/// ghi log Error kèm mã đơn và mã điều; đơn vừa đổi thì chưa xét. Job quét cả DB nên chỉ kiểm đơn của mình; sửa lại dữ liệu trước khi bộ kiểm
/// sau test chạy.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Booking")]
[Trait("Catalog", "OB-BE-5")]
public sealed class BookingInvariantJobHttpTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
    : PaymentCancellationTestBase(database, host)
{
    private async Task<IReadOnlyList<RecordingLoggerProvider.Entry>> RunJobAsync()
    {
        var factory = await GetFactoryAsync();
        factory.Logs.Clear();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBookingInvariantJob>().CheckRecentBookingsAsync();
        return factory.Logs.Entries.Where(e => e.Category.EndsWith(nameof(BookingInvariantJob), StringComparison.Ordinal)).ToList();
    }

    private Task SetCourtsActiveAsync(Guid bookingId, bool active, DateTime updatedAt) =>
        WithDbAsync(async db =>
        {
            await db.BookingCourts.Where(bc => bc.BookingId == bookingId).ExecuteUpdateAsync(s => s.SetProperty(bc => bc.IsActive, active));
            // Lùi cả created_at: đơn vừa tạo cũng là "vừa đổi"
            await db.Bookings.Where(b => b.Id == bookingId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.UpdatedAt, updatedAt).SetProperty(b => b.CreatedAt, updatedAt));
            return 0;
        });

    [Fact]
    public async Task SettledBrokenOrder_IsLoggedWithItsBookingAndRule_RecentlyChangedOrderIsNot()
    {
        var court = await SeedCourtAsync();
        var otherCourt = await SeedCourtAsync();
        var settled = await CreateWalkInAsync(await SeedStaffAsync(court.BranchId), court, payNow: false);
        var recent = await CreateWalkInAsync(await SeedStaffAsync(otherCourt.BranchId), otherCourt, payNow: false);
        var now = DateTime.UtcNow;
        try
        {
            // Đơn hiệu lực có dòng sân bị tắt: một đơn yên 10 phút, một đơn vừa đổi 30 giây trước
            await SetCourtsActiveAsync(settled, active: false, now.AddMinutes(-10));
            await SetCourtsActiveAsync(recent, active: false, now.AddSeconds(-30));

            var logs = await RunJobAsync();

            Assert.DoesNotContain(logs, e => e.Exception != null);
            Assert.Contains(logs, e => e.Level == LogLevel.Error
                                       && e.Message.Contains(settled.ToString()) && e.Message.Contains(InvariantRules.ActiveCourtsAreActive));
            Assert.DoesNotContain(logs, e => e.Message.Contains(recent.ToString()));
        }
        finally
        {
            await SetCourtsActiveAsync(settled, active: true, now.AddMinutes(-10));
            await SetCourtsActiveAsync(recent, active: true, now.AddSeconds(-30));
        }
    }
}
