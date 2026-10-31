using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>BE-UT-09 (phần truy vấn): repository lịch sử khách trả đúng dữ liệu thô trên schema thật.</summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-UT-09")]
public sealed class CustomerHistoryRepositoryTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly BaseDate = new(2026, 9, 1);

    private int _dayOffset;

    private CustomerHistoryRepository Repository => new(DbContext);

    // Mỗi booking một ngày riêng để không đụng ràng buộc loại trừ chồng giờ của cùng một sân
    private DateOnly NextDate() => BaseDate.AddDays(_dayOffset++);

    private async Task<Booking> AddBookingAsync(
        BranchSeedData seed,
        User customer,
        BookingStatus status,
        BookingCancelReason? reason = null,
        DateOnly? courtDate = null,
        TimeOnly? courtEnd = null,
        DateTime? cancelledAt = null)
    {
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, status);
        booking.CancelReason = reason;
        if (status == BookingStatus.PENDING_APPROVAL) // chk_bookings_pending_approval
        {
            booking.TadssDecisionId = Guid.NewGuid();
            booking.ApprovalExpiresAt = DateTime.UtcNow.AddMinutes(30);
        }

        if (cancelledAt != null)
            booking.CancelledAt = cancelledAt;
        DbContext.Bookings.Add(booking);
        if (courtDate != null)
            DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(
                booking.Id, seed.Court.Id, courtDate.Value, new TimeOnly(18, 0), courtEnd ?? new TimeOnly(20, 0)));
        await DbContext.SaveChangesAsync();
        return booking;
    }

    [Fact]
    public async Task GetEntriesAsync_WhenCustomerHasNoBookings_ReturnsEmpty()
    {
        var customer = await Seeder.SeedCustomerAsync();

        var entries = await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid());

        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetEntriesAsync_WhenCompletedHasSeveralCourts_ReturnsLatestDateAndEndTime()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = await AddBookingAsync(seed, customer, BookingStatus.COMPLETED, courtDate: new DateOnly(2026, 9, 3), courtEnd: new TimeOnly(21, 0));
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(booking.Id, seed.Court.Id, new DateOnly(2026, 9, 5), new TimeOnly(8, 0), new TimeOnly(9, 30)));
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(booking.Id, seed.Court.Id, new DateOnly(2026, 9, 5), new TimeOnly(10, 0), new TimeOnly(11, 0), isActive: false));
        await DbContext.SaveChangesAsync();

        var entry = Assert.Single(await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid()));

        Assert.Equal(BookingStatus.COMPLETED, entry.Status);
        Assert.Equal(new DateOnly(2026, 9, 5), entry.LastPlayDate);
        Assert.Equal(new TimeOnly(11, 0), entry.LastPlayEndTime); // dòng không hoạt động vẫn tính như bản export Phase 5
    }

    [Fact]
    public async Task GetEntriesAsync_WhenCompletedHasNoCourt_ReturnsNullPlayEnd()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        await AddBookingAsync(seed, customer, BookingStatus.COMPLETED);

        var entry = Assert.Single(await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid()));

        Assert.Null(entry.LastPlayDate);
        Assert.Null(entry.LastPlayEndTime);
    }

    [Fact]
    public async Task GetEntriesAsync_WhenBookingsAreOpenOrBelongToOthersOrExcluded_ReturnsOnlyRelevantOnes()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var other = await Seeder.SeedCustomerAsync();
        await AddBookingAsync(seed, customer, BookingStatus.NO_SHOW, courtDate: NextDate());
        var excluded = await AddBookingAsync(seed, customer, BookingStatus.COMPLETED, courtDate: NextDate());
        await AddBookingAsync(seed, customer, BookingStatus.PENDING, courtDate: NextDate());
        await AddBookingAsync(seed, customer, BookingStatus.CONFIRMED, courtDate: NextDate());
        await AddBookingAsync(seed, customer, BookingStatus.PENDING_APPROVAL);
        await AddBookingAsync(seed, other, BookingStatus.COMPLETED, courtDate: NextDate());

        var entries = await Repository.GetEntriesAsync(customer.Id, excluded.Id);

        var entry = Assert.Single(entries);
        Assert.Equal(BookingStatus.NO_SHOW, entry.Status);
    }

    [Fact]
    public async Task GetEntriesAsync_WhenCustomerCancelledAfterPaying_ReturnsPaymentFlagAndLargestRefundPercent()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var cancelledAt = new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);
        var booking = await AddBookingAsync(
            seed, customer, BookingStatus.CANCELLED_REFUNDED, BookingCancelReason.CUSTOMER_CANCELLED, NextDate(), cancelledAt: cancelledAt);
        await Seeder.SeedPaymentAsync(booking, PaymentTxStatus.REFUNDED, 30m, 70m);

        var entry = Assert.Single(await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid()));

        Assert.Equal(BookingCancelReason.CUSTOMER_CANCELLED, entry.CancelReason);
        Assert.Equal(cancelledAt, entry.CancelledAt);
        Assert.Equal(DateTimeKind.Utc, entry.CancelledAt!.Value.Kind);
        Assert.True(entry.HasSuccessfulPayment);
        Assert.Equal(70m, entry.MaxRefundPercent);
    }

    [Fact]
    public async Task GetEntriesAsync_WhenInvoiceHasOnlyPendingOrFailedPayments_ReportsNotPaid()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = await AddBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.PAYMENT_EXPIRED, NextDate());
        var pending = await Seeder.SeedPaymentAsync(booking, PaymentTxStatus.PENDING);
        DbContext.Payments.Add(TestDataSeeder.NewPayment(pending.InvoiceId, PaymentTxStatus.FAILED));
        await DbContext.SaveChangesAsync();

        var entry = Assert.Single(await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid()));

        Assert.False(entry.HasSuccessfulPayment);
        Assert.Null(entry.MaxRefundPercent);
    }

    [Fact]
    public async Task GetEntriesAsync_WhenCancelledWithoutInvoice_ReturnsNotPaid()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        await AddBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.SYSTEM_CLEANUP);

        var entry = Assert.Single(await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid()));

        Assert.False(entry.HasSuccessfulPayment);
        Assert.Null(entry.MaxRefundPercent);
        Assert.Equal(BookingCancelReason.SYSTEM_CLEANUP, entry.CancelReason);
    }

    [Fact]
    public async Task GetEntriesAsync_WhenCancelledWithoutReason_ReturnsNullReason()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        await AddBookingAsync(seed, customer, BookingStatus.CANCELLED);

        var entry = Assert.Single(await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid()));

        Assert.Null(entry.CancelReason);
    }

    [Fact]
    public async Task GetEntriesAsync_WhenCombinedWithCalculator_CountsMixedHistoryByCancelReason()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var cancelledAt = new DateTime(2026, 9, 2, 3, 0, 0, DateTimeKind.Utc);

        await AddBookingAsync(seed, customer, BookingStatus.COMPLETED, courtDate: new DateOnly(2026, 9, 10), courtEnd: new TimeOnly(20, 0));
        await AddBookingAsync(seed, customer, BookingStatus.COMPLETED, courtDate: new DateOnly(2026, 9, 12), courtEnd: new TimeOnly(19, 0));
        await AddBookingAsync(seed, customer, BookingStatus.NO_SHOW, courtDate: new DateOnly(2026, 9, 14));
        // khách hủy chưa trả tiền → cancelled
        await AddBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.CUSTOMER_CANCELLED, cancelledAt: cancelledAt);
        // khách hủy đã trả, hoàn 50 % → late
        var late = await AddBookingAsync(seed, customer, BookingStatus.CANCELLED_REFUNDED, BookingCancelReason.CUSTOMER_CANCELLED, cancelledAt: cancelledAt);
        await Seeder.SeedPaymentAsync(late, PaymentTxStatus.REFUNDED, 50m);
        // khách hủy đã trả, hoàn 100 % → cancelled
        var full = await AddBookingAsync(seed, customer, BookingStatus.CANCELLED_REFUNDED, BookingCancelReason.CUSTOMER_CANCELLED, cancelledAt: cancelledAt);
        await Seeder.SeedPaymentAsync(full, PaymentTxStatus.REFUNDED, 100m);
        await AddBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.PAYMENT_EXPIRED, cancelledAt: cancelledAt);
        await AddBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.APPROVAL_EXPIRED, cancelledAt: cancelledAt);
        await AddBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.STAFF_CANCELLED, cancelledAt: cancelledAt);

        var entries = await Repository.GetEntriesAsync(customer.Id, Guid.NewGuid());
        var summary = CustomerHistoryCalculator.Calculate(entries, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(9, entries.Count);
        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(1, summary.NoShowCount);
        Assert.Equal(2, summary.CancelledCount);
        Assert.Equal(1, summary.LateCancelledCount);
        Assert.Equal(1, summary.PaymentExpiredCount);
        Assert.Equal(new DateTime(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc), summary.LastCompletedAtUtc); // 19:00 VN ngày 12/9
    }
}
