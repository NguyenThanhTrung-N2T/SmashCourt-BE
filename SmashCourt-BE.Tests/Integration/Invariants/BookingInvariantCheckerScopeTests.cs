using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// BE-IT-20, phần hạ tầng của bộ kiểm (plan 6A-6 Task 9): phạm vi theo id và theo thời gian (<c>created_at</c> hoặc <c>updated_at</c>),
/// đầu vào sai, snapshot REPEATABLE READ, và những điều DB đã tự chặn (ràng buộc thật của schema).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-20")]
public sealed class BookingInvariantCheckerScopeTests(PostgreSqlIntegrationFixture fixture) : InvariantTestBase(fixture)
{
    private async Task<PendingOrder> SeedBrokenOrderAsync(DateTime createdAt, DateTime updatedAt)
    {
        var order = await SeedOrderAsync();
        order.Invoice.HoldDeadline = null; // Phụ-2
        order.Booking.CreatedAt = createdAt;
        order.Booking.UpdatedAt = updatedAt;
        await DbContext.SaveChangesAsync();
        return order;
    }

    private Task<IReadOnlyList<InvariantViolation>> CheckWindowAsync(DateTime from, DateTime to) =>
        BookingInvariantChecker.CheckAsync(DbContext, BookingScope.Between(from, to), Now);

    // ───────────────────────── Phạm vi ─────────────────────────

    [Fact]
    public async Task ViolatingOrderOutsideTheIdList_IsNotReported()
    {
        var broken = await SeedBrokenOrderAsync(CreatedAt, CreatedAt);
        var valid = await SeedOrderAsync();

        AssertNone(await CheckAsync(valid.Booking.Id));
        AssertOnly(await CheckAsync(valid.Booking.Id, broken.Booking.Id), InvariantRules.PendingHasHoldDeadline, broken.Booking.Id);
    }

    [Fact]
    public async Task EmptyIdList_ReturnsNothing()
    {
        await SeedBrokenOrderAsync(CreatedAt, CreatedAt);

        AssertNone(await CheckAsync());
    }

    [Fact]
    public async Task TimeWindow_IncludesOrdersCreatedOrUpdatedInsideIt()
    {
        var windowFrom = CreatedAt.AddHours(-1);
        var windowTo = CreatedAt.AddHours(1);
        var created = await SeedBrokenOrderAsync(CreatedAt, CreatedAt);
        var touched = await SeedBrokenOrderAsync(CreatedAt.AddDays(-3), CreatedAt);      // đơn có sẵn bị kịch bản sửa
        var createdAtEdge = await SeedBrokenOrderAsync(windowTo, windowTo);               // biên đóng
        var before = await SeedBrokenOrderAsync(CreatedAt.AddDays(-3), CreatedAt.AddDays(-3));
        var after = await SeedBrokenOrderAsync(windowTo.AddTicks(10), windowTo.AddTicks(10));

        var reported = (await CheckWindowAsync(windowFrom, windowTo)).Select(v => v.BookingId).ToHashSet();

        Assert.Contains(created.Booking.Id, reported);
        Assert.Contains(touched.Booking.Id, reported);
        Assert.Contains(createdAtEdge.Booking.Id, reported);
        Assert.DoesNotContain(before.Booking.Id, reported);
        Assert.DoesNotContain(after.Booking.Id, reported);
    }

    // Hai nhánh của phạm vi kiểm riêng ở cả hai biên (mỗi đơn chỉ có đúng một mốc nằm trong khoảng)
    [Theory]
    [InlineData(true, true)]   // created_at = biên đầu, updated_at sau khoảng
    [InlineData(true, false)]  // created_at = biên cuối, updated_at sau khoảng
    [InlineData(false, true)]  // created_at trước khoảng, updated_at = biên đầu
    [InlineData(false, false)] // created_at trước khoảng, updated_at = biên cuối
    public async Task TimeWindow_BothBoundsAreClosed_ForEachTimestamp(bool viaCreatedAt, bool atStart)
    {
        var windowFrom = CreatedAt.AddHours(-1);
        var windowTo = CreatedAt.AddHours(1);
        var edge = atStart ? windowFrom : windowTo;
        var order = viaCreatedAt
            ? await SeedBrokenOrderAsync(edge, windowTo.AddDays(1))
            : await SeedBrokenOrderAsync(windowFrom.AddDays(-1), edge);

        var violations = await CheckWindowAsync(windowFrom, windowTo);

        var mine = Assert.Single(violations, v => v.BookingId == order.Booking.Id);
        Assert.Equal(InvariantRules.PendingHasHoldDeadline, mine.Rule);
    }

    [Fact]
    public async Task InvalidScopeOrClock_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BookingInvariantChecker.CheckAsync(DbContext, new BookingScope(null, null, null), Now));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BookingInvariantChecker.CheckAsync(DbContext, new BookingScope(null, Now, null), Now));
        await Assert.ThrowsAsync<ArgumentException>(() => CheckWindowAsync(Now, Now.AddTicks(-1)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            CheckWindowAsync(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), Now.AddHours(1)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            BookingInvariantChecker.CheckAsync(DbContext, BookingScope.Of(Guid.NewGuid()), DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    // ───────────────────────── Snapshot ─────────────────────────

    [Fact]
    public async Task WithoutATransaction_ReadsInsideARepeatableReadTransaction_ThenCloses()
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmashCourtContext>();
        Assert.Null(db.Database.CurrentTransaction);

        var isolation = await BookingInvariantChecker.InSnapshotAsync(db, () =>
            Task.FromResult(db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel));

        Assert.Equal(IsolationLevel.RepeatableRead, isolation);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task InsideAnExistingTransaction_ReusesIt()
    {
        var existing = DbContext.Database.CurrentTransaction;
        Assert.NotNull(existing);

        var used = await BookingInvariantChecker.InSnapshotAsync(DbContext, () => Task.FromResult(DbContext.Database.CurrentTransaction));

        Assert.Same(existing, used);
        Assert.Same(existing, DbContext.Database.CurrentTransaction);
    }

    [Fact]
    public async Task CheckOnAContextWithoutTransaction_Works()
    {
        using var scope = Fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmashCourtContext>();

        var violations = await BookingInvariantChecker.CheckAsync(db, BookingScope.Of(Guid.NewGuid()), Now);

        Assert.Empty(violations);
        Assert.Null(db.Database.CurrentTransaction);
    }

    // ───────────────────────── Điều DB đã chặn (không cần kiểm trong C#) ─────────────────────────

    private async Task<string?> ConstraintOfFailedSaveAsync()
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());
        return Assert.IsType<PostgresException>(ex.InnerException).ConstraintName;
    }

    [Fact]
    public async Task Db_PendingApprovalWithoutDecision_IsRejected()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        DbContext.Bookings.Add(TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING_APPROVAL, CreatedAt));

        Assert.Equal("chk_bookings_pending_approval", await ConstraintOfFailedSaveAsync());
    }

    [Fact]
    public async Task Db_TwoActiveOverlappingLines_AreRejected()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        await SeedLineAsync(seed, seed.Court, BookingStatus.CONFIRMED, "08:00", "09:00");
        var customer = await Seeder.SeedCustomerAsync();
        var second = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.CONFIRMED, CreatedAt);
        DbContext.Bookings.Add(second);
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(
            second.Id, seed.Court.Id, PlayDate, new TimeOnly(8, 30), new TimeOnly(9, 30)));

        Assert.Equal("excl_booking_courts_no_overlap", await ConstraintOfFailedSaveAsync());
    }

    [Fact]
    public async Task Db_ReasonOnANonCancelledOrder_IsRejected()
    {
        var order = await SeedOrderAsync(BookingStatus.CONFIRMED);
        order.Booking.CancelReason = BookingCancelReason.STAFF_CANCELLED;

        Assert.Equal("chk_bookings_cancel_reason", await ConstraintOfFailedSaveAsync());
    }
}
