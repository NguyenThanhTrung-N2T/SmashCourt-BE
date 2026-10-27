using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>BookingDepositService với repository thật trên schema thật: dữ liệu DB đi hết đường ống ra request gửi TADSS.</summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-UT-02")]
public sealed class BookingDepositServiceIntegrationTests(PostgreSqlIntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime CreatedAt = new(2026, 11, 18, 16, 55, 0, DateTimeKind.Utc); // 23:55 giờ VN
    private static readonly DateOnly PlayDate = new(2026, 11, 20);

    private TadssDecisionRequestDto? _sent;
    private Guid? _sentKey;

    private BookingDepositService CreateService(bool enableTadss = false, TadssCallResult<TadssDecisionResultDto>? reply = null)
    {
        var client = new Mock<ITadssClient>();
        client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<TadssDecisionRequestDto, Guid, CancellationToken>((request, key, _) => (_sent, _sentKey) = (request, key))
            .ReturnsAsync(reply ?? TadssCallResult<TadssDecisionResultDto>.Failure(TadssFailureKind.Timeout, "corr"));
        var monitor = new Mock<IOptionsMonitor<TadssSettings>>();
        monitor.Setup(x => x.CurrentValue).Returns(new TadssSettings { BranchTimeZone = "Asia/Ho_Chi_Minh", EnableTadssDecision = enableTadss });
        var paymentMonitor = new Mock<IOptionsMonitor<BookingPaymentSettings>>();
        paymentMonitor.Setup(x => x.CurrentValue).Returns(new BookingPaymentSettings());
        return new BookingDepositService(
            new BookingRepository(DbContext), new CustomerHistoryRepository(DbContext), client.Object, monitor.Object,
            paymentMonitor.Object, TimeProvider.System, NullLogger<BookingDepositService>.Instance);
    }

    private async Task<Booking> AddHistoryBookingAsync(
        BranchSeedData seed, User customer, BookingStatus status, BookingCancelReason? reason, DateTime createdAt, DateTime? cancelledAt, DateOnly? playDate)
    {
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, status, createdAt);
        booking.CancelReason = reason;
        if (cancelledAt != null)
            booking.CancelledAt = cancelledAt;
        DbContext.Bookings.Add(booking);
        if (playDate != null)
            DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(booking.Id, seed.Court.Id, playDate.Value, new TimeOnly(18, 0), new TimeOnly(20, 0)));
        await DbContext.SaveChangesAsync();
        return booking;
    }

    [Fact]
    public async Task EvaluateAsync_WhenBookingHasHistoryTierPromotionAndTwoCourts_SendsFullRequestBuiltFromDatabase()
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var secondCourt = await Seeder.SeedCourtAsync(seed);
        var memberSince = new DateTime(2025, 3, 14, 1, 0, 0, DateTimeKind.Utc);
        var customer = await Seeder.SeedCustomerAsync(memberSince);
        await Seeder.SeedTierAsync(500);
        await Seeder.SeedTierAsync(2_000);
        await Seeder.SeedCustomerLoyaltyAsync(customer, await Seeder.SeedTierAsync(500)); // hạng 0, 100, 500, 2000 ⇒ thứ hạng 3 ⇒ GOLD

        // Lịch sử: 1 đơn hoàn tất (xong trước khi tạo), 1 hủy hết hạn thanh toán, 1 khách hủy chưa trả,
        // 1 đơn hoàn tất xong SAU khi tạo (không tính), 1 hủy sau khi tạo (không tính)
        await AddHistoryBookingAsync(seed, customer, BookingStatus.COMPLETED, null, CreatedAt.AddDays(-20), null, new DateOnly(2026, 10, 30));
        await AddHistoryBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.PAYMENT_EXPIRED, CreatedAt.AddDays(-5), CreatedAt.AddDays(-5).AddMinutes(10), null);
        await AddHistoryBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.CUSTOMER_CANCELLED, CreatedAt.AddDays(-3), CreatedAt.AddDays(-3).AddHours(1), null);
        await AddHistoryBookingAsync(seed, customer, BookingStatus.COMPLETED, null, CreatedAt.AddDays(-1), null, new DateOnly(2026, 11, 25));
        await AddHistoryBookingAsync(seed, customer, BookingStatus.CANCELLED, BookingCancelReason.PAYMENT_EXPIRED, CreatedAt.AddDays(-1), CreatedAt.AddMinutes(1), null);

        // Booking đang xét: 2 sân, 2 khung giờ, giảm giá, khuyến mãi
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, CreatedAt);
        DbContext.Bookings.Add(booking);
        var invoice = TestDataSeeder.NewInvoice(booking, courtFee: 190_000m);
        invoice.PromotionDiscountAmount = 10_000m;
        invoice.FinalTotal = 180_000m;
        DbContext.Invoices.Add(invoice);
        var promotion = await Seeder.SeedPromotionAsync("WINTER");
        DbContext.BookingPromotions.Add(TestDataSeeder.NewBookingPromotion(booking.Id, promotion));
        var slot1 = await Seeder.SeedTimeSlotAsync(new TimeOnly(18, 0), new TimeOnly(19, 0));
        var slot2 = await Seeder.SeedTimeSlotAsync(new TimeOnly(19, 0), new TimeOnly(20, 0));
        foreach (var court in new[] { seed.Court, secondCourt })
        {
            var line = TestDataSeeder.NewBookingCourt(booking.Id, court.Id, PlayDate, new TimeOnly(18, 0), new TimeOnly(20, 0));
            DbContext.BookingCourts.Add(line);
            await DbContext.SaveChangesAsync();
            DbContext.BookingPriceItems.Add(TestDataSeeder.NewPriceItem(line.Id, slot1.Id, court == seed.Court ? 50_000m : 40_000m));
            DbContext.BookingPriceItems.Add(TestDataSeeder.NewPriceItem(line.Id, slot2.Id, court == seed.Court ? 60_000m : 40_000m));
        }

        await DbContext.SaveChangesAsync();

        var result = await CreateService().EvaluateAsync(booking.Id);

        Assert.Equal(TadssFailureKind.Timeout, result.FailureKind); // kết quả của client được trả nguyên
        Assert.Equal(booking.Id, _sentKey);
        var request = _sent!;
        Assert.Equal(booking.Id, request.ReservationId);
        Assert.Equal(seed.Branch.Id, request.BranchId);
        Assert.Equal(180_000m, request.TotalPrice);
        Assert.Equal(TadssChannel.ONLINE, request.Channel);
        Assert.Equal(2, request.Items.Count);
        Assert.Equal(190_000m, request.Items.Sum(i => i.Price));
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7)), request.Items[0].StartTime);
        Assert.Equal(new DateTimeOffset(CreatedAt, TimeSpan.Zero), request.RequestedAt);
        Assert.Equal(new DateTimeOffset(memberSince, TimeSpan.Zero), request.Customer.MemberSince);
        Assert.Equal(TadssTier.GOLD, request.Customer.Tier);
        Assert.False(request.Customer.IsGuest);
        Assert.Equal("WINTER", request.Metadata!["promotionCode"]);
        var history = request.Customer.History!;
        Assert.Equal((1, 0, 1, 0, 1), (history.CompletedCount, history.NoShowCount, history.CancelledCount, history.LateCancelledCount, history.PaymentExpiredCount));
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.FromHours(7)), history.LastCompletedAt);

        var schema = TadssContractFiles.Evaluate(TadssContractFiles.DecisionRequest, JsonSerializer.Serialize(request, TadssJson.Options));
        Assert.True(schema.IsValid, TadssContractFiles.Describe(schema));
    }

    [Fact]
    public async Task EvaluateAsync_WhenBookingDoesNotExist_ThrowsAndNeverCallsTadss()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(Guid.NewGuid()));

        Assert.Null(_sent);
    }

    // ── DecideAsync trên DB thật (đóng ghi chú rà soát 22/10: trước đây chỉ có unit test) ──

    // Giờ chơi xa trong tương lai: các ca dưới không xét hạn duyệt (P6-24)
    private static readonly DateTime FarPlayStart = DateTime.UtcNow.AddDays(30);

    private async Task<Booking> SeedSimpleBookingAsync(decimal finalTotal)
    {
        var seed = await Seeder.SeedBranchWithCourtAsync();
        var customer = await Seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, CreatedAt);
        DbContext.Bookings.Add(booking);
        DbContext.Invoices.Add(TestDataSeeder.NewInvoice(booking, finalTotal));
        DbContext.BookingCourts.Add(TestDataSeeder.NewBookingCourt(booking.Id, seed.Court.Id, PlayDate, new TimeOnly(18, 0), new TimeOnly(19, 0)));
        await DbContext.SaveChangesAsync();
        return booking;
    }

    private static TadssDecisionResultDto Approved(Guid bookingId, decimal amount, decimal rate) => new()
    {
        DecisionId = Guid.NewGuid(), ReservationId = bookingId, Status = TadssDecisionStatus.APPROVED,
        Deposit = new TadssDepositDto { Rate = rate, Amount = amount, Currency = "VND" },
        StrategyName = "DefaultDecisionStrategy", StrategyVersion = "2.1.0", ExecutionType = "AUTOMATED", FallbackUsed = false,
        ReasonCode = "TIER_DEPOSIT", Reasons = [], Recommendations = [], EvaluatedAt = new DateTimeOffset(CreatedAt, TimeSpan.Zero)
    };

    [Fact]
    [Trait("Catalog", "BE-IT-02")]
    public async Task DecideAsync_WhenFlagOnAndTadssApproves_BuildsRequestFromDatabaseAndReturnsTadssDeposit()
    {
        var booking = await SeedSimpleBookingAsync(200_000m);
        var approved = Approved(booking.Id, 60_000m, 0.3m);

        var decision = await CreateService(true, TadssCallResult<TadssDecisionResultDto>.Success(approved, 200, "corr")).DecideAsync(booking.Id, 200_000m, FarPlayStart);

        Assert.Equal(DepositBranch.TadssDeposit, decision.Branch);
        Assert.Equal(approved.DecisionId, decision.TadssDecisionId);
        Assert.Equal(60_000m, decision.DepositAmount);
        Assert.Equal(booking.Id, _sentKey);
        Assert.Equal(200_000m, _sent!.TotalPrice);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenFlagOnButBookingMissing_FallsBackWithoutCallingTadss()
    {
        // F2: lỗi dựng request không làm hỏng luồng đặt sân
        var decision = await CreateService(true).DecideAsync(Guid.NewGuid(), 200_000m, FarPlayStart);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(LogLevel.Error, decision.LogLevel);
        Assert.Null(_sent);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenTadssDepositExceedsOrderTotal_FallsBackToFullPayment()
    {
        // F1: TADSS trả cọc lớn hơn tổng đơn ⇒ không thu quá tổng tiền
        var booking = await SeedSimpleBookingAsync(200_000m);

        var decision = await CreateService(true, TadssCallResult<TadssDecisionResultDto>.Success(Approved(booking.Id, 250_000m, 1m), 200, "corr"))
            .DecideAsync(booking.Id, 200_000m, FarPlayStart);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(200_000m, decision.DepositAmount);
        Assert.Null(decision.TadssDecisionId);
    }
}
