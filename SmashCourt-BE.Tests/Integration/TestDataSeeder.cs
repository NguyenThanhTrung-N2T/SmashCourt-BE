using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration;

public sealed class TestDataSeeder
{
    private readonly SmashCourtContext _context;

    public TestDataSeeder(SmashCourtContext context)
    {
        _context = context;
    }

    public async Task<BasicSeedData> SeedBasicDataAsync()
    {
        var now = DateTime.UtcNow;
        var bronzeTier = new LoyaltyTier
        {
            Id = Guid.NewGuid(),
            Name = $"Bronze-{Guid.NewGuid():N}",
            MinPoints = 0,
            DiscountRate = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        var silverTier = new LoyaltyTier
        {
            Id = Guid.NewGuid(),
            Name = $"Silver-{Guid.NewGuid():N}",
            MinPoints = 100,
            DiscountRate = 5,
            CreatedAt = now,
            UpdatedAt = now
        };
        var timeSlot = new TimeSlot
        {
            Id = Guid.NewGuid(),
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 0),
            DayType = DayType.WEEKDAY,
            CreatedAt = now
        };
        var courtType = new CourtType
        {
            Id = Guid.NewGuid(),
            Name = $"Single-{Guid.NewGuid():N}",
            Description = "Integration test court type",
            Status = CourtTypeStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.AddRange(bronzeTier, silverTier, timeSlot, courtType);
        await _context.SaveChangesAsync();

        return new BasicSeedData(bronzeTier, silverTier, timeSlot, courtType);
    }

    public async Task<BranchSeedData> SeedBranchWithCourtAsync(CourtType? courtType = null)
    {
        courtType ??= await _context.CourtTypes.FirstOrDefaultAsync(x => x.Status == CourtTypeStatus.ACTIVE)
            ?? (await SeedBasicDataAsync()).CourtType;

        var now = DateTime.UtcNow;
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            Name = $"Test Branch-{Guid.NewGuid():N}",
            Address = "123 Test Street",
            Phone = "0900000000",
            OpenTime = new TimeOnly(6, 0),
            CloseTime = new TimeOnly(23, 0),
            Status = BranchStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        var court = new Court
        {
            Id = Guid.NewGuid(),
            BranchId = branch.Id,
            CourtTypeId = courtType.Id,
            Name = $"Court-{Guid.NewGuid():N}",
            Status = CourtStatus.AVAILABLE,
            CreatedAt = now,
            UpdatedAt = now
        };
        var branchCourtType = new BranchCourtType
        {
            Id = Guid.NewGuid(),
            BranchId = branch.Id,
            CourtTypeId = courtType.Id,
            IsActive = true,
            CreatedAt = now
        };

        _context.AddRange(branch, court, branchCourtType);
        await _context.SaveChangesAsync();

        return new BranchSeedData(branch, court, courtType);
    }

    /// <summary>Thêm một sân cùng loại vào chi nhánh đã có (dùng cho booking nhiều sân).</summary>
    public async Task<Court> SeedCourtAsync(BranchSeedData seed)
    {
        var now = DateTime.UtcNow;
        var court = new Court
        {
            Id = Guid.NewGuid(),
            BranchId = seed.Branch.Id,
            CourtTypeId = seed.CourtType.Id,
            Name = $"Court-{Guid.NewGuid():N}",
            Status = CourtStatus.AVAILABLE,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Courts.Add(court);
        await _context.SaveChangesAsync();
        return court;
    }

    /// <summary>Tạo hóa đơn + một giao dịch thanh toán cho booking đã lưu, kèm các khoản hoàn với tỷ lệ cho trước.</summary>
    public async Task<Payment> SeedPaymentAsync(Booking booking, PaymentTxStatus status, params decimal[] refundPercents)
    {
        var invoice = NewInvoice(booking);
        _context.Invoices.Add(invoice);
        var payment = NewPayment(invoice.Id, status);
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        foreach (var percent in refundPercents)
            _context.Refunds.Add(NewRefund(payment.Id, percent));
        await _context.SaveChangesAsync();
        return payment;
    }

    /// <summary>Tạo khách hàng (CUSTOMER) hợp lệ với schema thật.</summary>
    public async Task<User> SeedCustomerAsync(DateTime? createdAt = null)
    {
        var now = createdAt ?? DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"customer-{Guid.NewGuid():N}@test.local",
            FullName = "Khách Test",
            Role = UserRole.CUSTOMER,
            Status = UserStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Dựng (chưa lưu) booking hợp lệ với các ràng buộc CHECK của schema thật.</summary>
    public static Booking NewBooking(Guid branchId, Guid customerId, BookingStatus status = BookingStatus.PENDING, DateTime? createdAt = null)
    {
        var now = createdAt ?? DateTime.UtcNow;
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingCode = $"BK{Guid.NewGuid():N}"[..20],
            BranchId = branchId,
            CustomerId = customerId,
            BookingDate = DateOnly.FromDateTime(now),
            Status = status,
            Source = BookingSource.ONLINE,
            CreatedAt = now,
            UpdatedAt = now
        };

        // chk_bookings_cancelled_at: trạng thái hủy bắt buộc có cancelled_at
        if (status is BookingStatus.CANCELLED or BookingStatus.CANCELLED_PENDING_REFUND or BookingStatus.CANCELLED_REFUNDED)
            booking.CancelledAt = now;

        return booking;
    }

    /// <summary>
    /// Tạo hạng thành viên với mức điểm tối thiểu cho trước; min_points là duy nhất nên dùng lại hạng đã có
    /// (<see cref="SeedBasicDataAsync"/> đã tạo hạng 0 và 100, và <see cref="SeedBranchWithCourtAsync"/> gọi nó khi chưa có loại sân).
    /// </summary>
    public async Task<LoyaltyTier> SeedTierAsync(int minPoints)
    {
        var existing = await _context.LoyaltyTiers.FirstOrDefaultAsync(t => t.MinPoints == minPoints);
        if (existing != null)
            return existing;

        var now = DateTime.UtcNow;
        var tier = new LoyaltyTier
        {
            Id = Guid.NewGuid(),
            Name = $"Tier{minPoints}-{Guid.NewGuid():N}",
            MinPoints = minPoints,
            DiscountRate = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.LoyaltyTiers.Add(tier);
        await _context.SaveChangesAsync();
        return tier;
    }

    /// <summary>Gán hạng cho khách (customer_loyalty là duy nhất theo user).</summary>
    public async Task SeedCustomerLoyaltyAsync(User customer, LoyaltyTier tier)
    {
        var now = DateTime.UtcNow;
        _context.CustomerLoyalties.Add(new CustomerLoyalty
        {
            Id = Guid.NewGuid(), UserId = customer.Id, TierId = tier.Id, TotalPoints = tier.MinPoints, CreatedAt = now, UpdatedAt = now
        });
        await _context.SaveChangesAsync();
    }

    /// <summary>Tạo khung giờ giá ngày thường; (start_time, end_time, day_type) là duy nhất nên dùng lại khung đã có (như 08:00–09:00 của <see cref="SeedBasicDataAsync"/>).</summary>
    public async Task<TimeSlot> SeedTimeSlotAsync(TimeOnly start, TimeOnly end)
    {
        var existing = await _context.TimeSlots.FirstOrDefaultAsync(t => t.StartTime == start && t.EndTime == end && t.DayType == DayType.WEEKDAY);
        if (existing != null)
            return existing;

        var slot = new TimeSlot { Id = Guid.NewGuid(), StartTime = start, EndTime = end, DayType = DayType.WEEKDAY, CreatedAt = DateTime.UtcNow };
        _context.TimeSlots.Add(slot);
        await _context.SaveChangesAsync();
        return slot;
    }

    /// <summary>Tạo khuyến mãi tối thiểu hợp lệ với mã cho trước.</summary>
    public async Task<Promotion> SeedPromotionAsync(string? code)
    {
        var now = DateTime.UtcNow;
        var promotion = new Promotion
        {
            Id = Guid.NewGuid(),
            Name = $"Khuyến mãi {Guid.NewGuid():N}"[..30],
            Code = code,
            DiscountType = DiscountTypeEnum.PERCENT,
            DiscountValue = 10,
            StartDate = DateOnly.FromDateTime(now).AddDays(-1),
            EndDate = DateOnly.FromDateTime(now).AddDays(30),
            Status = PromotionStatus.ACTIVE,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.Promotions.Add(promotion);
        await _context.SaveChangesAsync();
        return promotion;
    }

    /// <summary>Dựng (chưa lưu) bản chụp khuyến mãi của booking.</summary>
    public static BookingPromotion NewBookingPromotion(Guid bookingId, Promotion promotion) => new()
    {
        Id = Guid.NewGuid(),
        BookingId = bookingId,
        PromotionId = promotion.Id,
        PromotionNameSnapshot = promotion.Name,
        PromotionCodeSnapshot = promotion.Code,
        DiscountTypeSnapshot = promotion.DiscountType,
        DiscountValueSnapshot = promotion.DiscountValue,
        DiscountAmount = 10_000m,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>Dựng (chưa lưu) giá của một khung giờ trong dòng sân (unit_price phải dương).</summary>
    public static BookingPriceItem NewPriceItem(Guid bookingCourtId, Guid timeSlotId, decimal unitPrice) => new()
    {
        Id = Guid.NewGuid(), BookingCourtId = bookingCourtId, TimeSlotId = timeSlotId, UnitPrice = unitPrice, CreatedAt = DateTime.UtcNow
    };

    /// <summary>Dựng (chưa lưu) dòng sân của booking; ràng buộc loại trừ chồng giờ áp theo (sân, ngày) nên mỗi booking nên dùng ngày riêng.</summary>
    public static BookingCourt NewBookingCourt(
        Guid bookingId, Guid courtId, DateOnly date, TimeOnly start, TimeOnly end, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        BookingId = bookingId,
        CourtId = courtId,
        Date = date,
        StartTime = start,
        EndTime = end,
        IsActive = isActive,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>Dựng (chưa lưu) slot lock; excl_slot_locks_no_overlap áp theo (sân, ngày, khoảng giờ).</summary>
    public static SlotLock NewSlotLock(
        Guid bookingId, Guid courtId, DateOnly date, TimeOnly start, TimeOnly end, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        BookingId = bookingId,
        CourtId = courtId,
        Date = date,
        StartTime = start,
        EndTime = end,
        ExpiresAt = expiresAt,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>Dựng (chưa lưu) hóa đơn hợp lệ (chk_invoices_final_total_formula: không giảm giá, không phí dịch vụ).</summary>
    public static Invoice NewInvoice(Booking booking, decimal courtFee = 100_000m) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceCode = $"INV{Guid.NewGuid():N}"[..20],
        BookingId = booking.Id,
        CourtFee = courtFee,
        FinalTotal = courtFee,
        PaymentStatus = InvoicePaymentStatus.UNPAID,
        PaymentTiming = PaymentTiming.PREPAID,
        CreatedAt = booking.CreatedAt,
        UpdatedAt = booking.CreatedAt
    };

    /// <summary>Dựng (chưa lưu) giao dịch thanh toán; SUCCESS bắt buộc có paid_at (chk_payments_paid_at).</summary>
    public static Payment NewPayment(Guid invoiceId, PaymentTxStatus status, decimal amount = 100_000m) => new()
    {
        Id = Guid.NewGuid(),
        InvoiceId = invoiceId,
        Method = PaymentTxMethod.VNPAY,
        Amount = amount,
        Status = status,
        PaidAt = status == PaymentTxStatus.SUCCESS ? DateTime.UtcNow : null,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    /// <summary>Dựng (chưa lưu) khoản hoàn tiền với tỷ lệ cho trước.</summary>
    public static Refund NewRefund(Guid paymentId, decimal refundPercent, decimal amount = 10_000m) => new()
    {
        Id = Guid.NewGuid(),
        PaymentId = paymentId,
        Amount = amount,
        RefundPercent = refundPercent,
        Status = RefundStatus.COMPLETED,
        CreatedAt = DateTime.UtcNow
    };
}

public sealed record BasicSeedData(
    LoyaltyTier BronzeTier,
    LoyaltyTier SilverTier,
    TimeSlot TimeSlot,
    CourtType CourtType);

public sealed record BranchSeedData(
    Branch Branch,
    Court Court,
    CourtType CourtType);
