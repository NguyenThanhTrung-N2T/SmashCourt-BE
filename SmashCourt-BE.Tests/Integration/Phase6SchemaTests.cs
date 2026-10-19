using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// BE-IT-01: schema Phase 6 (enum PENDING_APPROVAL, trường cọc, trường duyệt) đọc/ghi qua EF trên schema thật.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-01")]
public sealed class Phase6SchemaTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    // PostgreSQL lưu microsecond; làm tròn trước khi so sánh
    private static DateTime UtcMicros(DateTime value) => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private async Task<(Booking Booking, User Customer, BranchSeedData Branch)> SeedBookingAsync(BookingStatus status)
    {
        var branch = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(branch.Branch.Id, customer.Id, status);
        return (booking, customer, branch);
    }

    private static Invoice NewInvoice(Booking booking) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceCode = $"INV{Guid.NewGuid():N}"[..20],
        BookingId = booking.Id,
        CourtFee = 100_000m,
        FinalTotal = 100_000m,
        PaymentStatus = InvoicePaymentStatus.UNPAID,
        PaymentTiming = PaymentTiming.PREPAID,
        CreatedAt = booking.CreatedAt,
        UpdatedAt = booking.CreatedAt
    };

    private static string? ConstraintOf(DbUpdateException ex) => (ex.InnerException as PostgresException)?.ConstraintName;

    [Fact]
    public async Task Booking_WhenPendingApprovalWithAllApprovalFields_RoundTripsThroughEf()
    {
        var (booking, manager, _) = await SeedBookingAsync(BookingStatus.PENDING_APPROVAL);
        var decisionId = Guid.NewGuid();
        var expiresAt = UtcMicros(DateTime.UtcNow.AddMinutes(30));
        booking.TadssDecisionId = decisionId;
        booking.ApprovalExpiresAt = expiresAt;
        booking.ApprovalReasonCode = "HIGH_VALUE";
        booking.ApprovalReasons = ["Đơn từ 2.000.000 đ", "Khách không đến 2 lần"];
        DbContext.Bookings.Add(booking);
        await DbContext.SaveChangesAsync();

        var persisted = await DbContext.Bookings.AsNoTracking().SingleAsync(x => x.Id == booking.Id);

        Assert.Equal(BookingStatus.PENDING_APPROVAL, persisted.Status);
        Assert.Equal(decisionId, persisted.TadssDecisionId);
        Assert.Equal(expiresAt, persisted.ApprovalExpiresAt);
        Assert.Equal(DateTimeKind.Utc, persisted.ApprovalExpiresAt!.Value.Kind);
        Assert.Equal("HIGH_VALUE", persisted.ApprovalReasonCode);
        Assert.Equal(["Đơn từ 2.000.000 đ", "Khách không đến 2 lần"], persisted.ApprovalReasons);
        Assert.Null(persisted.ApprovalResult);
        Assert.Null(persisted.CancelReason);
        Assert.Null(persisted.TadssOutcomeReportedAt);
        _ = manager;
    }

    [Fact]
    public async Task Booking_WhenCancelledWithResolution_RoundTripsResolutionAndCancelReason()
    {
        var (booking, manager, _) = await SeedBookingAsync(BookingStatus.CANCELLED);
        var resolvedAt = UtcMicros(DateTime.UtcNow);
        booking.TadssDecisionId = Guid.NewGuid();
        booking.ApprovalExpiresAt = resolvedAt.AddMinutes(10);
        booking.ApprovalResult = ApprovalResult.REJECTED;
        booking.ApprovalResolvedBy = manager.Id;
        booking.ApprovalResolvedAt = resolvedAt;
        booking.ApprovalResolutionNote = "Sân bảo trì";
        booking.CancelSource = CancelSourceEnum.STAFF;
        booking.CancelReason = BookingCancelReason.APPROVAL_REJECTED;
        booking.TadssOutcomeReportedAt = resolvedAt;
        DbContext.Bookings.Add(booking);
        await DbContext.SaveChangesAsync();

        var persisted = await DbContext.Bookings.AsNoTracking().SingleAsync(x => x.Id == booking.Id);

        Assert.Equal(ApprovalResult.REJECTED, persisted.ApprovalResult);
        Assert.Equal(manager.Id, persisted.ApprovalResolvedBy);
        Assert.Equal(resolvedAt, persisted.ApprovalResolvedAt);
        Assert.Equal("Sân bảo trì", persisted.ApprovalResolutionNote);
        Assert.Equal(BookingCancelReason.APPROVAL_REJECTED, persisted.CancelReason);
        Assert.Equal(resolvedAt, persisted.TadssOutcomeReportedAt);
    }

    [Fact]
    public async Task Invoice_WhenDepositFieldsSet_RoundTripsRateAmountSourceAndHoldDeadline()
    {
        var (booking, _, _) = await SeedBookingAsync(BookingStatus.PENDING);
        var holdDeadline = UtcMicros(DateTime.UtcNow.AddMinutes(15));
        var invoice = NewInvoice(booking);
        invoice.ExpiresAt = holdDeadline.AddMinutes(-5);
        invoice.DepositRate = 0.3m;
        invoice.DepositAmount = 30_000m;
        invoice.DepositSource = DepositSource.MANAGER;
        invoice.HoldDeadline = holdDeadline;
        DbContext.Bookings.Add(booking);
        DbContext.Invoices.Add(invoice);
        await DbContext.SaveChangesAsync();

        var persisted = await DbContext.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);

        Assert.Equal(0.3m, persisted.DepositRate);
        Assert.Equal(30_000m, persisted.DepositAmount);
        Assert.Equal(DepositSource.MANAGER, persisted.DepositSource);
        Assert.Equal(holdDeadline, persisted.HoldDeadline);
        Assert.Equal(DateTimeKind.Utc, persisted.HoldDeadline!.Value.Kind);
    }

    [Fact]
    public async Task Invoice_WhenNoDepositFields_ReadsNullsForLegacyOrganicInvoices()
    {
        var (booking, _, _) = await SeedBookingAsync(BookingStatus.PENDING);
        var invoice = NewInvoice(booking);
        DbContext.Bookings.Add(booking);
        DbContext.Invoices.Add(invoice);
        await DbContext.SaveChangesAsync();

        var persisted = await DbContext.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);

        Assert.Null(persisted.DepositRate);
        Assert.Null(persisted.DepositAmount);
        Assert.Null(persisted.DepositSource);
        Assert.Null(persisted.HoldDeadline);
    }

    [Fact]
    public async Task Booking_WhenPendingApprovalWithoutDecisionId_IsRejectedByDatabase()
    {
        var (booking, _, _) = await SeedBookingAsync(BookingStatus.PENDING_APPROVAL);
        booking.ApprovalExpiresAt = DateTime.UtcNow.AddMinutes(30); // thiếu TadssDecisionId
        DbContext.Bookings.Add(booking);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());

        Assert.Equal("chk_bookings_pending_approval", ConstraintOf(ex));
    }

    [Fact]
    public async Task Booking_WhenCancelReasonOnNonCancelledBooking_IsRejectedByDatabase()
    {
        var (booking, _, _) = await SeedBookingAsync(BookingStatus.PENDING);
        booking.CancelReason = BookingCancelReason.PAYMENT_EXPIRED;
        DbContext.Bookings.Add(booking);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());

        Assert.Equal("chk_bookings_cancel_reason", ConstraintOf(ex));
    }

    [Fact]
    public async Task Booking_WhenApprovalResultWithoutResolvedAt_IsRejectedByDatabase()
    {
        var (booking, _, _) = await SeedBookingAsync(BookingStatus.PENDING);
        booking.ApprovalResult = ApprovalResult.APPROVED; // thiếu ApprovalResolvedAt
        DbContext.Bookings.Add(booking);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());

        Assert.Equal("chk_bookings_approval_resolution", ConstraintOf(ex));
    }

    [Fact]
    public async Task Invoice_WhenDepositAmountWithoutSource_IsRejectedByDatabase()
    {
        var (booking, _, _) = await SeedBookingAsync(BookingStatus.PENDING);
        var invoice = NewInvoice(booking);
        invoice.DepositAmount = 30_000m; // thiếu DepositRate và DepositSource
        DbContext.Bookings.Add(booking);
        DbContext.Invoices.Add(invoice);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());

        Assert.Equal("chk_invoices_deposit_fields", ConstraintOf(ex));
    }

    [Fact]
    public async Task Invoice_WhenDepositRateAboveOne_IsRejectedByDatabase()
    {
        var (booking, _, _) = await SeedBookingAsync(BookingStatus.PENDING);
        var invoice = NewInvoice(booking);
        invoice.DepositRate = 1.5m;
        invoice.DepositAmount = 30_000m;
        invoice.DepositSource = DepositSource.TADSS;
        DbContext.Bookings.Add(booking);
        DbContext.Invoices.Add(invoice);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => DbContext.SaveChangesAsync());

        Assert.Equal("chk_invoices_deposit_rate_range", ConstraintOf(ex));
    }

    [Fact]
    public async Task Booking_WhenTwoCourtsEachHaveSlotLock_PersistsBothLocks()
    {
        // Hồi quy P6-13: quan hệ 1-1 cũ làm EF xóa lock thứ nhất khi thêm lock thứ hai (đặt online nhiều sân)
        var (booking, _, seed) = await SeedBookingAsync(BookingStatus.PENDING);
        var now = DateTime.UtcNow;
        var secondCourt = new Court
        {
            Id = Guid.NewGuid(),
            BranchId = seed.Branch.Id,
            CourtTypeId = seed.CourtType.Id,
            Name = $"Court-{Guid.NewGuid():N}",
            Status = CourtStatus.AVAILABLE,
            CreatedAt = now,
            UpdatedAt = now
        };
        DbContext.Courts.Add(secondCourt);
        DbContext.Bookings.Add(booking);
        await DbContext.SaveChangesAsync();

        foreach (var court in new[] { seed.Court, secondCourt })
        {
            // Add tường minh như SlotLockRepository.CreateAsync (khóa Guid đã gán nên không dùng navigation)
            DbContext.SlotLocks.Add(new SlotLock
            {
                Id = Guid.NewGuid(),
                BookingId = booking.Id,
                CourtId = court.Id,
                Date = booking.BookingDate,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(9, 0),
                ExpiresAt = now.AddMinutes(10),
                CreatedAt = now
            });
        }

        await DbContext.SaveChangesAsync();

        var loaded = await DbContext.Bookings.AsNoTracking().Include(x => x.SlotLocks).SingleAsync(x => x.Id == booking.Id);
        var courtIds = loaded.SlotLocks.Select(x => x.CourtId).ToList();
        Assert.Equal(2, courtIds.Count);
        Assert.Contains(seed.Court.Id, courtIds);
        Assert.Contains(secondCourt.Id, courtIds);
    }
}
