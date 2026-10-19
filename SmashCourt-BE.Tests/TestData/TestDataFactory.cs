using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Promotions;
using SmashCourt_BE.DTOs.PriceConfig;

namespace SmashCourt_BE.Tests.TestData;

internal static class TestDataFactory
{
    public static Promotion CreatePromotion(
        DiscountTypeEnum discountType = DiscountTypeEnum.PERCENT,
        decimal? discountValue = null,
        decimal? maxDiscountAmount = null,
        string? code = null,
        PromotionStatus status = PromotionStatus.ACTIVE,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        int? usageLimit = null,
        int usedCount = 0)
    {
        return new Promotion
        {
            Id = Guid.NewGuid(),
            Code = code ?? TestConstants.TestPromotionCode,
            Name = "Test promotion",
            DiscountType = discountType,
            DiscountValue = discountValue ?? TestConstants.StandardDiscountPercent,
            MaxDiscountAmount = maxDiscountAmount,
            StartDate = startDate ?? new DateOnly(2026, 1, 1),
            EndDate = endDate ?? new DateOnly(2026, 12, 31),
            Status = status,
            UsageLimit = usageLimit,
            UsedCount = usedCount,
            Conditions = new List<PromotionCondition>()
        };
    }

    public static PromotionContext CreatePromotionContext(
        decimal? bookingAmount = null,
        DateTime? bookingDate = null,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null,
        string? sport = null,
        Guid? userId = null,
        Guid? branchId = null,
        int previousBookingCount = 0)
    {
        return new PromotionContext
        {
            UserId = userId ?? Guid.NewGuid(),
            BranchId = branchId ?? Guid.NewGuid(),
            BookingAmount = bookingAmount ?? TestConstants.StandardBookingAmount,
            BookingDate = bookingDate ?? TestConstants.StandardDateTime,
            StartTime = startTime ?? TestConstants.EveningStartTime,
            EndTime = endTime ?? TestConstants.EveningEndTime,
            Sport = sport ?? TestConstants.DefaultSport,
            PreviousBookingCount = previousBookingCount
        };
    }

    public static User CreateUser(
        string? email = null,
        string? fullName = null,
        UserRole role = UserRole.CUSTOMER,
        UserStatus status = UserStatus.ACTIVE,
        bool isEmailVerified = true,
        string? passwordHash = null,
        int failedLoginCount = 0,
        DateTime? lockedUntil = null,
        bool mustChangePassword = false,
        bool is2FAEnabled = false)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email ?? TestConstants.TestEmail,
            FullName = fullName ?? TestConstants.TestFullName,
            Role = role,
            Status = status,
            IsEmailVerified = isEmailVerified,
            PasswordHash = passwordHash,
            FailedLoginCount = failedLoginCount,
            LockedUntil = lockedUntil,
            MustChangePassword = mustChangePassword,
            Is2faEnabled = is2FAEnabled,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Booking CreateBooking(
        Guid? customerId = null,
        Guid? branchId = null,
        BookingStatus status = BookingStatus.PENDING,
        decimal totalAmount = 0,
        DateOnly? bookingDate = null,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        return new Booking
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId ?? Guid.NewGuid(),
            BranchId = branchId ?? Guid.NewGuid(),
            Status = status,
            BookingDate = bookingDate ?? TestConstants.StandardTestDate,
            CreatedAt = DateTime.UtcNow,
            BookingCode = "TEST-BOOKING"
        };
    }

    public static Court CreateCourt(
        Guid? branchId = null,
        string? courtName = null,
        string sport = TestConstants.DefaultSport,
        CourtStatus status = CourtStatus.AVAILABLE)
    {
        return new Court
        {
            Id = Guid.NewGuid(),
            BranchId = branchId ?? Guid.NewGuid(),
            Name = courtName ?? "Court 1",
            Status = status,
            CourtTypeId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static CalculatePriceResultDto CreatePriceResult(
        IEnumerable<Guid> courtIds,
        decimal feePerCourt = TestConstants.StandardCourtPrice,
        TimeSpan? startTime = null,
        TimeSpan? endTime = null)
    {
        var start = startTime ?? TestConstants.EveningStartTime;
        var end = endTime ?? TestConstants.EveningEndTime;

        return new CalculatePriceResultDto
        {
            TotalFee = courtIds.Count() * feePerCourt,
            Courts = courtIds.Select(courtId => new CourtPriceResultDto
            {
                CourtId = courtId,
                CourtName = "Test court",
                CourtFee = feePerCourt,
                Breakdown =
                [
                    new PriceBreakdownDto
                    {
                        StartTime = start,
                        EndTime = end,
                        UnitPrice = feePerCourt,
                        Hours = (decimal)(end - start).TotalHours,
                        SubTotal = feePerCourt,
                        PriceSource = "SYSTEM_PRICE"
                    }
                ]
            }).ToList()
        };
    }

    public static Invoice CreateInvoice(
        Guid? bookingId = null,
        decimal finalTotal = TestConstants.StandardBookingAmount,
        InvoicePaymentStatus paymentStatus = InvoicePaymentStatus.UNPAID,
        PaymentTiming paymentTiming = PaymentTiming.POSTPAID,
        DateTime? expiresAt = null,
        IEnumerable<Payment>? payments = null)
    {
        return new Invoice
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId ?? Guid.NewGuid(),
            InvoiceCode = "TEST-INVOICE",
            CourtFee = finalTotal,
            FinalTotal = finalTotal,
            PaymentStatus = paymentStatus,
            PaymentTiming = paymentTiming,
            ExpiresAt = expiresAt,
            Payments = payments?.ToList() ?? []
        };
    }

    public static Payment CreatePayment(
        Guid? invoiceId = null,
        decimal amount = TestConstants.StandardBookingAmount,
        PaymentTxStatus status = PaymentTxStatus.SUCCESS,
        PaymentTxMethod method = PaymentTxMethod.CASH)
    {
        return new Payment
        {
            Id = Guid.NewGuid(),
            InvoiceId = invoiceId ?? Guid.NewGuid(),
            Amount = amount,
            Method = method,
            Status = status,
            TransactionRef = "TEST-TRANSACTION",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
