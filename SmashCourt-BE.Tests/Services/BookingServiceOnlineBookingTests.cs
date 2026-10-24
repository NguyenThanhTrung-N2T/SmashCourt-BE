using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

/// <summary>
/// Gói 6A-2c Task 1: <c>CreateOnlineAsync</c> hai transaction (spec 7.6). Transaction 1 giữ chỗ không Payment; quyết định cọc ngoài transaction
/// với token không hủy được (D6, K-01); transaction 2 theo nhánh a/a′/b/c/d; thua câu cổng ⇒ 409 (D7, G-05); a′ có token và email (D8); A9.
/// </summary>
[TestCategory(TestCategories.Booking)]
public class BookingServiceOnlineBookingTests
{
    // Dữ liệu mẫu: 15/06/2026, 18:00–19:30 giờ VN (11:00Z); "bây giờ" mặc định của builder là 08:00 giờ VN (01:00Z)
    private static readonly DateOnly PlayDate = new(2026, 6, 15);
    private static readonly TimeSpan PlayStart = new(18, 0, 0);
    private static readonly TimeSpan PlayEnd = new(19, 30, 0);
    private static readonly DateTime PlayStartUtc = new(2026, 6, 15, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NowUtc = BookingServiceTestBuilder.DefaultNow.UtcDateTime;

    private const string RejectedMessage =
        "Rất tiếc, đơn đặt sân này không được chấp nhận. Vui lòng chọn khung giờ khác hoặc liên hệ chi nhánh.";

    /// <summary>
    /// Một đơn online đặt được: mock đủ cho transaction 1, quyết định cọc theo <see cref="Decision"/> và bốn câu cổng (thắng theo
    /// <see cref="GateWins"/>). <see cref="Steps"/> ghi thứ tự các bước để kiểm "không tạo Payment trước khi có quyết định".
    /// </summary>
    private sealed class Scenario
    {
        public BookingServiceTestBuilder Builder { get; } = new();
        public ListLogger<SmashCourt_BE.Services.BookingService> Logger { get; } = new();
        public User Customer { get; } = TestDataFactory.CreateUser(email: "khach@example.com", fullName: "Khách Online");
        public Court Court { get; } = TestDataFactory.CreateCourt(courtName: "Sân Một");
        public Court SecondCourt { get; }
        public Guid BookingId { get; } = Guid.NewGuid();
        public Guid InvoiceId { get; } = Guid.NewGuid();
        public Promotion? Promotion { get; private set; }
        public Booking? CreatedBooking { get; private set; }
        public Invoice? CreatedInvoice { get; private set; }
        public List<SlotLock> Locks { get; } = [];
        public List<string> Steps { get; } = [];
        public CancellationToken? DecideToken { get; private set; }
        public bool GateWins { get; set; } = true;
        public DepositBranch? LastBranch { get; private set; }
        public List<(string Event, BookingNotificationDto Payload)> BookingEvents { get; } = [];
        public List<(string Event, BookingNotificationDto Payload)> StaffOnlyEvents { get; } = [];
        public List<BookingPendingApprovalNotificationDto> PendingApprovalEvents { get; } = [];
        public List<PendingApprovalQueueChangedNotificationDto> QueueEvents { get; } = [];

        /// <summary>Quyết định cọc theo finalTotal; mặc định như cờ tắt (dự phòng 100 %, a′ khi dưới mức tối thiểu).</summary>
        public Func<decimal, DepositDecision> Decision { get; set; }

        public Scenario(decimal pricePerCourt = 200_000m)
        {
            SecondCourt = TestDataFactory.CreateCourt(branchId: Court.BranchId, courtName: "Sân Hai");
            Decision = finalTotal => DepositDecisionResolver.Resolve(null, finalTotal, Builder.PaymentSettings.MinOnlinePaymentAmount);
            var b = Builder;
            b.Logger = Logger;

            b.UserRepository.Setup(x => x.GetUserByIdAsync(Customer.Id)).ReturnsAsync(Customer);
            b.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>())).ReturnsAsync([Court, SecondCourt]);
            b.PriceService.Setup(x => x.CalculateForBookingAsync(Court.BranchId, It.IsAny<CalculatePriceDto>()))
                .ReturnsAsync((Guid? _, CalculatePriceDto dto) =>
                    TestDataFactory.CreatePriceResult(dto.Courts, pricePerCourt, dto.StartTime, dto.EndTime));
            b.TimeSlotRepository.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
            b.CodeGeneratorService.Setup(x => x.GenerateBookingCodeAsync()).ReturnsAsync("BK-ONLINE");
            b.CodeGeneratorService.Setup(x => x.GenerateInvoiceCodeAsync()).ReturnsAsync("INV-ONLINE");

            b.BookingRepository.Setup(x => x.CreateAsync(It.IsAny<Booking>()))
                .ReturnsAsync((Booking booking) =>
                {
                    booking.Id = BookingId;
                    CreatedBooking = booking;
                    Steps.Add("booking");
                    return booking;
                });
            b.BookingRepository.Setup(x => x.AddCourtAsync(It.IsAny<BookingCourt>()))
                .ReturnsAsync((BookingCourt bookingCourt) =>
                {
                    bookingCourt.Id = Guid.NewGuid();
                    return bookingCourt;
                });
            b.InvoiceRepository.Setup(x => x.CreateAsync(It.IsAny<Invoice>()))
                .ReturnsAsync((Invoice invoice) =>
                {
                    invoice.Id = InvoiceId;
                    CreatedInvoice = invoice;
                    Steps.Add("invoice");
                    return invoice;
                });
            b.SlotLockRepository.Setup(x => x.CreateAsync(It.IsAny<SlotLock>()))
                .ReturnsAsync((SlotLock slotLock) =>
                {
                    Locks.Add(slotLock);
                    Steps.Add("lock");
                    return slotLock;
                });
            b.UnitOfWork.Setup(x => x.ClearChangeTracker()).Callback(() => Steps.Add("clear"));

            b.DepositService.Setup(x => x.DecideAsync(BookingId, It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, decimal finalTotal, CancellationToken token) =>
                {
                    DecideToken = token;
                    Steps.Add("decide");
                    var decision = Decision(finalTotal);
                    LastBranch = decision.Branch;
                    return decision;
                });

            b.BookingRepository.Setup(x => x.ApplyPaymentRequiredAsync(
                    BookingId, It.IsAny<Guid?>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<DepositSource>(), It.IsAny<DateTime>()))
                .ReturnsAsync(() => Gate("gate-payment"));
            b.BookingRepository.Setup(x => x.ConfirmWithoutOnlinePaymentAsync(
                    BookingId, It.IsAny<Guid?>(), It.IsAny<DepositSource>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync(() => Gate("gate-confirm"));
            b.BookingRepository.Setup(x => x.HoldForApprovalAsync(
                    BookingId, It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(),
                    It.IsAny<DateTime>()))
                .ReturnsAsync(() => Gate("gate-approval"));
            b.BookingRepository.Setup(x => x.RejectByPolicyAsync(BookingId, It.IsAny<Guid>(), It.IsAny<DateTime>()))
                .ReturnsAsync(() => Gate("gate-reject"));
            b.BookingRepository.Setup(x => x.GetBookingStatusAsync(BookingId)).ReturnsAsync(BookingStatus.CANCELLED);

            b.VnPayService.Setup(x => x.CreatePaymentUrl(BookingId.ToString(), It.IsAny<decimal>(), It.IsAny<string>()))
                .Returns((string _, decimal amount, string _) =>
                    new VnPayPaymentUrlResult { Url = $"https://vnpay.test/pay?amount={amount}", TransactionRef = "TX-ONLINE" });
            b.PaymentRepository.Setup(x => x.CreateAsync(It.IsAny<Payment>()))
                .ReturnsAsync((Payment payment) =>
                {
                    Steps.Add("payment");
                    return payment;
                });
            b.PromotionRepository.Setup(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()))
                .Callback(() => Steps.Add("promotion+1"))
                .Returns(Task.CompletedTask);
            b.Broadcast.Setup(x => x.BroadcastBookingEventAsync(
                    It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
                .Callback((string name, BookingNotificationDto payload, Booking _, bool _) =>
                {
                    Steps.Add("broadcast");
                    BookingEvents.Add((name, payload));
                })
                .Returns(Task.CompletedTask);
            b.Broadcast.Setup(x => x.BroadcastBookingEventToStaffAsync(
                    It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
                .Callback((string name, BookingNotificationDto payload, Booking _, bool _) =>
                {
                    Steps.Add("broadcast-staff");
                    StaffOnlyEvents.Add((name, payload));
                })
                .Returns(Task.CompletedTask);
            b.Broadcast.Setup(x => x.BroadcastBookingPendingApprovalAsync(It.IsAny<BookingPendingApprovalNotificationDto>()))
                .Callback((BookingPendingApprovalNotificationDto payload) =>
                {
                    Steps.Add("pending-approval");
                    PendingApprovalEvents.Add(payload);
                })
                .Returns(Task.CompletedTask);
            b.Broadcast.Setup(x => x.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()))
                .Callback((PendingApprovalQueueChangedNotificationDto payload) =>
                {
                    Steps.Add("queue-changed");
                    QueueEvents.Add(payload);
                })
                .Returns(Task.CompletedTask);
            b.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(BookingId)).ReturnsAsync(() => Persisted());
        }

        private bool Gate(string step)
        {
            Steps.Add(step);
            return GateWins;
        }

        /// <summary>Booking đọc lại sau transaction 2 (đủ navigation cho email và sự kiện), trạng thái theo nhánh vừa áp.</summary>
        public Booking Persisted(string? customerEmail = null)
        {
            var status = LastBranch switch
            {
                DepositBranch.ConfirmWithoutOnlinePayment => BookingStatus.CONFIRMED,
                DepositBranch.NeedApproval => BookingStatus.PENDING_APPROVAL,
                DepositBranch.Rejected => BookingStatus.CANCELLED,
                _ => BookingStatus.PENDING
            };
            var booking = TestDataFactory.CreateBooking(Customer.Id, Court.BranchId, status);
            if (status == BookingStatus.PENDING_APPROVAL)
                booking.ApprovalExpiresAt = ApprovalExpiresAtUtc;
            booking.Id = BookingId;
            booking.BookingCode = "BK-ONLINE";
            booking.BookingDate = PlayDate;
            booking.Customer = new User { Id = Customer.Id, Email = customerEmail ?? Customer.Email, FullName = Customer.FullName };
            booking.Branch = new Branch { Id = Court.BranchId, Name = "Chi nhánh 1" };
            booking.BookingCourts =
            [
                new BookingCourt
                {
                    CourtId = Court.Id, Court = Court, Date = PlayDate,
                    StartTime = TimeOnly.FromTimeSpan(PlayStart), EndTime = TimeOnly.FromTimeSpan(PlayEnd), IsActive = true
                }
            ];
            booking.Invoice = new Invoice
            {
                Id = InvoiceId, BookingId = BookingId, InvoiceCode = "INV-ONLINE", CourtFee = 200_000m, FinalTotal = 200_000m,
                PaymentStatus = InvoicePaymentStatus.UNPAID, PaymentTiming = PaymentTiming.POSTPAID
            };
            return booking;
        }

        /// <summary>Thêm khuyến mãi không điều kiện vào request.</summary>
        public Promotion UsePromotion(DiscountTypeEnum type, decimal value)
        {
            Promotion = TestDataFactory.CreatePromotion(type, value);
            Builder.PromotionRepository.Setup(x => x.GetByIdWithConditionsAsync(Promotion.Id)).ReturnsAsync(Promotion);
            return Promotion;
        }

        public CreateOnlineBookingDto Request(bool twoCourts = false) => new()
        {
            BookingDate = PlayDate.ToDateTime(TimeOnly.MinValue),
            PromotionId = Promotion?.Id,
            GuestName = "Không dùng",
            GuestEmail = "guest@example.com",
            Courts = (twoCourts ? new[] { Court, SecondCourt } : new[] { Court })
                .Select(court => new CourtSlotDto { CourtId = court.Id, StartTime = PlayStart, EndTime = PlayEnd })
                .ToList()
        };

        public Task<OnlineBookingResponse> RunAsync(CreateOnlineBookingDto? request = null) =>
            Builder.Build().CreateOnlineAsync(request ?? Request(), Customer.Id);

        public int IndexOf(string step) => Steps.IndexOf(step);
    }

    private static readonly Guid DecisionId = Guid.Parse("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11");

    private static DepositDecision TadssDeposit(decimal rate, decimal amount) => new(
        DepositBranch.TadssDeposit, DepositSource.TADSS, rate, amount, amount, DecisionId, null, null, [], LogLevel.None, null);

    private static DepositDecision TadssSmallDeposit(decimal amount) => new(
        DepositBranch.ConfirmWithoutOnlinePayment, DepositSource.TADSS, 0m, 0m, amount, DecisionId, null, null, [], LogLevel.Warning, "nhỏ");

    private static readonly DateTime ApprovalExpiresAtUtc = new(2026, 6, 15, 1, 20, 0, DateTimeKind.Utc);

    private static DepositDecision NeedApproval() => new(
        DepositBranch.NeedApproval, null, 0m, 0m, null, DecisionId, ApprovalExpiresAtUtc, "HIGH_VALUE", ["Giá trị đơn cao", "Khách mới"],
        LogLevel.Information, "duyệt");

    private static DepositDecision Rejected() => new(
        DepositBranch.Rejected, null, 0m, 0m, null, DecisionId, null, null, [], LogLevel.Information, "từ chối");

    /// <summary>Quyết định theo tên nhánh để chạy cùng một ca cho mọi nhánh (Theory).</summary>
    private static Func<decimal, DepositDecision> DecisionFor(string branch) => branch switch
    {
        "a" => _ => TadssDeposit(0.5m, 100_000m),
        "a'" => _ => TadssSmallDeposit(3_000m),
        "b" => _ => NeedApproval(),
        "c" => _ => Rejected(),
        "d" => total => DepositDecisionResolver.Resolve(null, total, 5_000m),
        _ => throw new ArgumentOutOfRangeException(nameof(branch), branch, null)
    };

    // ───────────────────────── A9: chặn khung giờ đã bắt đầu ─────────────────────────

    [Theory]
    [InlineData(1, true)]   // giờ bắt đầu = now − 1 s
    [InlineData(0, true)]   // giờ bắt đầu = now
    [InlineData(-1, false)] // giờ bắt đầu = now + 1 s
    [TestCategory(TestCategories.Validation)]
    public async Task A9_ComparesPlayStartWithNowToTheSecond(int secondsAfterStart, bool blocked)
    {
        var scenario = new Scenario();
        scenario.Builder.Time.SetUtcNow(new DateTimeOffset(PlayStartUtc).AddSeconds(secondsAfterStart));

        if (blocked)
        {
            var exception = await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync());
            exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "Khung giờ đã bắt đầu hoặc đã qua, vui lòng chọn khung giờ khác");
        }
        else
        {
            await scenario.RunAsync();
            Assert.Contains("decide", scenario.Steps);
        }
    }

    public static TheoryData<string, string, string, string, bool> A9Cases => new()
    {
        // ngày đặt (VN), giờ bắt đầu, giờ kết thúc, "bây giờ" (UTC), bị chặn
        { "2026-06-15", "18:00", "19:30", "2026-06-15T13:00:00Z", true },  // hôm nay 20:00 VN, khung 18:00 đã qua
        { "2026-06-15", "18:00", "19:30", "2026-06-15T10:30:00Z", false }, // hôm nay 17:30 VN, khung 18:00 sắp tới
        { "2026-06-16", "06:00", "07:00", "2026-06-15T16:00:00Z", false }, // 23:00 VN, ngày mai giờ sớm
        { "2026-06-15", "23:30", "23:59", "2026-06-15T16:29:59Z", false }, // khung cuối ngày, còn 1 s
        { "2026-06-15", "23:30", "23:59", "2026-06-15T16:30:00Z", true },
        { "2026-06-16", "00:00", "01:00", "2026-06-15T16:59:59Z", false }, // khung 00:00 VN, còn 1 s
        { "2026-06-16", "00:00", "01:00", "2026-06-15T17:00:00Z", true },
        // UTC còn là 15/06 nhưng VN đã sang 16/06 00:30: khung 00:00 hôm nay (VN) đã bắt đầu dù ngày đặt "sau" ngày UTC
        { "2026-06-16", "00:00", "01:00", "2026-06-15T17:30:00Z", true },
        { "2026-06-16", "06:00", "07:00", "2026-06-15T17:30:00Z", false },
    };

    [Theory]
    [MemberData(nameof(A9Cases))]
    [TestCategory(TestCategories.Validation)]
    public async Task A9_UsesVietnamWallClockForPlayStart(string date, string start, string end, string nowUtc, bool blocked)
    {
        var scenario = new Scenario();
        scenario.Builder.Time.SetUtcNow(DateTimeOffset.Parse(nowUtc));
        var request = scenario.Request();
        request.BookingDate = DateOnly.Parse(date).ToDateTime(TimeOnly.MinValue);
        request.Courts[0].StartTime = TimeSpan.Parse(start);
        request.Courts[0].EndTime = TimeSpan.Parse(end);

        if (blocked)
            (await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync(request)))
                .ShouldBeAppException(400, ErrorCodes.BadRequest, "Khung giờ đã bắt đầu");
        else
            await scenario.RunAsync(request);

        Assert.Equal(!blocked, scenario.Steps.Contains("decide"));
    }

    [Fact]
    [TestCategory(TestCategories.Validation)]
    public async Task A9_WhenBlocked_TouchesNothing_ForEveryCourtOfTheRequest()
    {
        var scenario = new Scenario();
        scenario.Builder.Time.SetUtcNow(new DateTimeOffset(PlayStartUtc).AddMinutes(5));

        var exception = await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync(scenario.Request(twoCourts: true)));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "Khung giờ đã bắt đầu");
        var b = scenario.Builder;
        b.CourtRepository.Verify(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()), Times.Never);
        b.SlotLockRepository.Verify(x => x.DeleteExpiredByBranchAsync(It.IsAny<Guid>()), Times.Never);
        b.BookingRepository.Verify(x => x.HasOverlapAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()), Times.Never);
        b.DepositService.Verify(x => x.DecideAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(scenario.Steps);
        b.SlotInterestRepository.Verify(x => x.CreateAsync(It.IsAny<SlotInterest>()), Times.Never);
    }

    // ───────────────────────── Transaction 1 và thứ tự ─────────────────────────

    [Fact]
    public async Task Transaction1_HoldsTheSlotWithoutPayment_AndUsesOneNowForCreatedAtAndHold()
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync(scenario.Request(twoCourts: true));

        var booking = Assert.IsType<Booking>(scenario.CreatedBooking);
        Assert.Equal(BookingStatus.PENDING, booking.Status);
        Assert.Equal(BookingSource.ONLINE, booking.Source);
        Assert.Equal(scenario.Customer.Id, booking.CustomerId);
        Assert.Equal(NowUtc, booking.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, booking.CreatedAt.Kind);

        var invoice = Assert.IsType<Invoice>(scenario.CreatedInvoice);
        Assert.Equal(PaymentTiming.PREPAID, invoice.PaymentTiming);
        Assert.Equal(NowUtc.AddMinutes(10), invoice.ExpiresAt);
        Assert.Equal(NowUtc.AddMinutes(15), invoice.HoldDeadline);
        Assert.Equal(400_000m, invoice.FinalTotal);

        // Một khóa mỗi sân, hết hạn cùng hóa đơn
        Assert.Equal(new[] { scenario.Court.Id, scenario.SecondCourt.Id }, scenario.Locks.Select(l => l.CourtId));
        Assert.All(scenario.Locks, l =>
        {
            Assert.Equal(invoice.ExpiresAt, l.ExpiresAt);
            Assert.Equal(NowUtc, l.CreatedAt);
            Assert.Equal(scenario.BookingId, l.BookingId);
        });

        scenario.Builder.DepositService.Verify(x => x.DecideAsync(scenario.BookingId, 400_000m, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(400_000m, result.FinalTotal);
    }

    [Fact]
    public async Task Transaction1_UsesConfiguredHoldMinutes()
    {
        var scenario = new Scenario();
        scenario.Builder.PaymentSettings.InitialHoldMinutes = 7;
        scenario.Builder.PaymentSettings.PaymentHoldMaxMinutes = 12;

        var result = await scenario.RunAsync();

        Assert.Equal(NowUtc.AddMinutes(7), scenario.CreatedInvoice?.ExpiresAt);
        Assert.Equal(NowUtc.AddMinutes(12), scenario.CreatedInvoice?.HoldDeadline);
        Assert.All(scenario.Locks, l => Assert.Equal(NowUtc.AddMinutes(7), l.ExpiresAt));
        Assert.Equal(NowUtc.AddMinutes(7), result.ExpiresAt);
    }

    [Fact]
    public async Task NoPaymentIsCreatedBeforeTheDecision_AndTrackerIsClearedBetweenTheTransactions()
    {
        var scenario = new Scenario();

        await scenario.RunAsync();

        Assert.Equal(
            new[] { "booking", "invoice", "lock", "clear", "decide", "gate-payment", "payment", "broadcast" },
            scenario.Steps);
    }

    [Fact]
    [Trait("Catalog", "K-01")]
    public async Task DecideAsync_ReceivesATokenThatCannotBeCanceled()
    {
        // D6: transaction 1 đã commit thì quyết định và transaction 2 chạy tới cùng dù khách ngắt kết nối
        var scenario = new Scenario();

        await scenario.RunAsync();

        var token = Assert.IsType<CancellationToken>(scenario.DecideToken);
        Assert.False(token.CanBeCanceled);
    }

    // ───────────────────────── Nhánh a, d: Payment = số tiền thu online ─────────────────────────

    [Fact]
    public async Task BranchD_WhenFlagIsOff_PaymentAndVnPayAmountAreTheFinalTotal()
    {
        var scenario = new Scenario();

        var result = await scenario.RunAsync();

        var b = scenario.Builder;
        b.BookingRepository.Verify(x => x.ApplyPaymentRequiredAsync(
            scenario.BookingId, null, 1m, 200_000m, DepositSource.FULL_PAYMENT_FALLBACK, NowUtc), Times.Once);
        b.VnPayService.Verify(x => x.CreatePaymentUrl(scenario.BookingId.ToString(), 200_000m, "Dat san San Mot"), Times.Once);
        b.PaymentRepository.Verify(x => x.CreateAsync(It.Is<Payment>(p =>
            p.InvoiceId == scenario.InvoiceId && p.Amount == 200_000m && p.Status == PaymentTxStatus.PENDING &&
            p.Method == PaymentTxMethod.VNPAY && p.TransactionRef == "TX-ONLINE")), Times.Once);
        Assert.Equal("https://vnpay.test/pay?amount=200000", result.PaymentUrl);
        Assert.Equal(NowUtc.AddMinutes(10), result.ExpiresAt);
        Assert.Equal(200_000m, result.FinalTotal);
    }

    [Fact]
    public async Task BranchA_TadssApproved_PaymentAndVnPayAmountAreTheDeposit()
    {
        var scenario = new Scenario { Decision = _ => TadssDeposit(0.5m, 100_000m) };

        var result = await scenario.RunAsync();

        var b = scenario.Builder;
        b.BookingRepository.Verify(x => x.ApplyPaymentRequiredAsync(
            scenario.BookingId, DecisionId, 0.5m, 100_000m, DepositSource.TADSS, NowUtc), Times.Once);
        b.VnPayService.Verify(x => x.CreatePaymentUrl(scenario.BookingId.ToString(), 100_000m, It.IsAny<string>()), Times.Once);
        b.PaymentRepository.Verify(x => x.CreateAsync(It.Is<Payment>(p => p.Amount == 100_000m)), Times.Once);
        Assert.Equal("https://vnpay.test/pay?amount=100000", result.PaymentUrl);
        Assert.Equal(200_000m, result.FinalTotal);
    }

    [Fact]
    public async Task Transaction2_UsesTheClockAfterTheDecisionForUpdatedAt()
    {
        var scenario = new Scenario();
        var decidedAt = NowUtc.AddSeconds(2);
        scenario.Decision = total =>
        {
            scenario.Builder.Time.SetUtcNow(decidedAt);
            return DepositDecisionResolver.Resolve(null, total, 5_000m);
        };

        await scenario.RunAsync();

        // Hạn thanh toán vẫn tính từ lúc giữ chỗ; mốc ghi của transaction 2 là lúc áp quyết định
        scenario.Builder.BookingRepository.Verify(x => x.ApplyPaymentRequiredAsync(
            scenario.BookingId, null, 1m, 200_000m, DepositSource.FULL_PAYMENT_FALLBACK, decidedAt), Times.Once);
        Assert.Equal(NowUtc.AddMinutes(10), scenario.CreatedInvoice?.ExpiresAt);
    }

    // ───────────────────────── Nhánh a′ (D3, D8, E2) ─────────────────────────

    [Fact]
    [TestCategory(TestCategories.Token)]
    [Trait("Catalog", "D8")]
    public async Task BranchAPrime_ConfirmsWithATokenAndSendsExactlyOneEmailCarryingTheRawToken()
    {
        var scenario = new Scenario { Decision = _ => TadssSmallDeposit(3_000m) };
        string? hash = null;
        DateTime? expiresAt = null;
        scenario.Builder.BookingRepository.Setup(x => x.ConfirmWithoutOnlinePaymentAsync(
                scenario.BookingId, DecisionId, DepositSource.TADSS, It.IsAny<string>(), It.IsAny<DateTime>(), NowUtc))
            .Callback((Guid _, Guid? _, DepositSource _, string h, DateTime e, DateTime _) => { hash = h; expiresAt = e; })
            .ReturnsAsync(true);

        var result = await scenario.RunAsync();

        var email = Assert.Single(scenario.Builder.Email.Sent);
        Assert.Equal("khach@example.com", email.To);
        var raw = System.Text.RegularExpressions.Regex.Match(email.Body, @"/booking/cancel\?token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        Assert.NotEmpty(raw);
        Assert.Equal(CancelTokenHelper.Hash(raw), hash);
        // Chơi trong 24 giờ tới ⇒ hết hạn đúng giờ bắt đầu
        Assert.Equal(PlayStartUtc, expiresAt);
        Assert.Equal(DateTimeKind.Utc, expiresAt?.Kind);

        Assert.Null(result.PaymentUrl);
        Assert.Null(result.ExpiresAt);
        Assert.Equal(200_000m, result.FinalTotal);
        scenario.Builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        scenario.Builder.VnPayService.Verify(x => x.CreatePaymentUrl(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    [TestCategory(TestCategories.Token)]
    public async Task BranchAPrime_WhenPlayIsDaysAway_TokenExpiresTwentyFourHoursAfterNow()
    {
        var scenario = new Scenario { Decision = _ => TadssSmallDeposit(0m) };
        var request = scenario.Request();
        request.BookingDate = new DateTime(2026, 6, 20);

        await scenario.RunAsync(request);

        scenario.Builder.BookingRepository.Verify(x => x.ConfirmWithoutOnlinePaymentAsync(
            scenario.BookingId, DecisionId, DepositSource.TADSS, It.IsAny<string>(), NowUtc.AddHours(24), NowUtc), Times.Once);
    }

    [Fact]
    public async Task BranchAPrime_WhenFlagIsOffAndTotalIsBelowMinimum_ConfirmsAsFallbackWithoutDecisionId()
    {
        // Z-10 ở mức unit: cờ tắt, finalTotal 3.000 < 5.000 ⇒ a′ nguồn FULL_PAYMENT_FALLBACK
        var scenario = new Scenario(pricePerCourt: 3_000m);

        var result = await scenario.RunAsync();

        scenario.Builder.BookingRepository.Verify(x => x.ConfirmWithoutOnlinePaymentAsync(
            scenario.BookingId, null, DepositSource.FULL_PAYMENT_FALLBACK, It.IsAny<string>(), It.IsAny<DateTime>(), NowUtc), Times.Once);
        Assert.Null(result.PaymentUrl);
        Assert.Single(scenario.Builder.Email.Sent);
    }

    [Fact]
    [TestCategory(TestCategories.Promotion)]
    public async Task BranchAPrime_WithPromotion_IncrementsUsageOnceInsideTransaction2()
    {
        // P-10 ở mức unit: mã giảm 100 % ⇒ finalTotal 0 ⇒ a′ (cờ tắt) ⇒ +1 lượt sau khi thắng cổng, trước khi gửi email
        var scenario = new Scenario();
        var promotion = scenario.UsePromotion(DiscountTypeEnum.PERCENT, 100m);

        var result = await scenario.RunAsync();

        Assert.Equal(0m, result.FinalTotal);
        scenario.Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(promotion.Id), Times.Once);
        Assert.True(scenario.IndexOf("gate-confirm") < scenario.IndexOf("promotion+1"));
        Assert.True(scenario.IndexOf("promotion+1") < scenario.IndexOf("broadcast"));
    }

    [Fact]
    public async Task BranchAPrime_WithoutPromotion_DoesNotTouchUsage()
    {
        var scenario = new Scenario { Decision = _ => TadssSmallDeposit(1_000m) };

        await scenario.RunAsync();

        scenario.Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    [TestCategory(TestCategories.Email)]
    public async Task BranchAPrime_WhenEmailFails_StillSucceeds()
    {
        var scenario = new Scenario { Decision = _ => TadssSmallDeposit(1_000m) };
        scenario.Builder.Email.FailOnSend = true;

        var result = await scenario.RunAsync();

        Assert.Equal(scenario.BookingId, result.BookingId);
        Assert.Null(result.PaymentUrl);
        Assert.Empty(scenario.Builder.Email.Sent);
        Assert.Contains(scenario.Logger.Entries, e => e.Level == LogLevel.Error && e.Exception != null &&
                                                      e.Message.Contains(scenario.BookingId.ToString()));
        Assert.Contains("broadcast", scenario.Steps);
    }

    [Fact]
    [TestCategory(TestCategories.Email)]
    public async Task BranchAPrime_WhenCustomerHasNoEmail_SkipsTheEmailAndStillSucceeds()
    {
        var scenario = new Scenario { Decision = _ => TadssSmallDeposit(1_000m) };
        scenario.Builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(scenario.BookingId))
            .ReturnsAsync(() => scenario.Persisted(customerEmail: ""));

        var result = await scenario.RunAsync();

        Assert.Null(result.PaymentUrl);
        Assert.Empty(scenario.Builder.Email.Sent);
        Assert.Contains(scenario.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(scenario.BookingId.ToString()));
    }

    [Fact]
    [TestCategory(TestCategories.Email)]
    public async Task BranchAPrime_WhenReloadingTheBookingFails_StillSucceedsWithoutEmailOrEvent()
    {
        var scenario = new Scenario { Decision = _ => TadssSmallDeposit(1_000m) };
        scenario.Builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(scenario.BookingId))
            .ThrowsAsync(new InvalidOperationException("DB tạm lỗi"));

        var result = await scenario.RunAsync();

        Assert.Equal(scenario.BookingId, result.BookingId);
        Assert.Empty(scenario.Builder.Email.Sent);
        Assert.DoesNotContain("broadcast", scenario.Steps);
    }

    // ───────────────────────── Nhánh b ─────────────────────────

    [Fact]
    public async Task BranchB_HoldsForApprovalWithTheDecisionData_WithoutPaymentOrEmail()
    {
        var scenario = new Scenario { Decision = _ => NeedApproval() };

        var result = await scenario.RunAsync();

        scenario.Builder.BookingRepository.Verify(x => x.HoldForApprovalAsync(
            scenario.BookingId, DecisionId, ApprovalExpiresAtUtc, "HIGH_VALUE",
            It.Is<IReadOnlyCollection<string>>(r => r.SequenceEqual(new[] { "Giá trị đơn cao", "Khách mới" })), NowUtc), Times.Once);
        Assert.Null(result.PaymentUrl);
        Assert.Null(result.ExpiresAt);
        scenario.Builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        Assert.Empty(scenario.Builder.Email.Sent);
    }

    [Fact]
    public async Task BranchB_WhenDecisionLacksApprovalData_FailsBeforeTouchingTheBooking()
    {
        var scenario = new Scenario { Decision = _ => NeedApproval() with { ApprovalReasonCode = null } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => scenario.RunAsync());

        scenario.Builder.BookingRepository.Verify(x => x.HoldForApprovalAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<DateTime>()), Times.Never);
    }

    // ───────────────────────── Nhánh c ─────────────────────────

    [Fact]
    public async Task BranchC_RejectsByPolicyAndThrowsBookingRejected_WithoutSlotInterest()
    {
        var scenario = new Scenario { Decision = _ => Rejected() };

        var exception = await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync());

        exception.ShouldBeAppException(400, ErrorCodes.BookingRejected);
        Assert.Equal(RejectedMessage, exception.Message);
        var b = scenario.Builder;
        b.BookingRepository.Verify(x => x.RejectByPolicyAsync(scenario.BookingId, DecisionId, NowUtc), Times.Once);
        // Không đi qua CreateSlotUnavailableExceptionAsync: không đọc email khách, không ghi slot_interest (R-8)
        b.UserRepository.Verify(x => x.GetUserByIdAsync(It.IsAny<Guid>()), Times.Never);
        b.SlotInterestRepository.Verify(x => x.ExistsAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>(), It.IsAny<string>()), Times.Never);
        b.SlotInterestRepository.Verify(x => x.CreateAsync(It.IsAny<SlotInterest>()), Times.Never);
        b.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        Assert.Empty(b.Email.Sent);
    }

    // ───────────────────────── Theo mọi nhánh ─────────────────────────

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    [TestCategory(TestCategories.Token)]
    [Trait("Catalog", "D8")]
    public async Task OnlyBranchAPrime_IssuesACancelTokenOrSendsTheConfirmationEmail(string branch)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch) };

        try
        {
            await scenario.RunAsync();
        }
        catch (AppException exception) when (branch == "c")
        {
            exception.ShouldBeAppException(400, ErrorCodes.BookingRejected);
        }

        scenario.Builder.BookingRepository.Verify(x => x.ConfirmWithoutOnlinePaymentAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DepositSource>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()),
            Times.Never);
        Assert.Empty(scenario.Builder.Email.Sent);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("c")]
    [InlineData("d")]
    [TestCategory(TestCategories.Promotion)]
    public async Task OnlyBranchAPrime_CountsThePromotionAtBookingTime(string branch)
    {
        // P-13…P-16 ở mức unit: a/d tăng lượt ở IPN, b chờ duyệt, c không tăng không giảm
        var scenario = new Scenario { Decision = DecisionFor(branch) };
        scenario.UsePromotion(DiscountTypeEnum.FIXED, 10_000m);

        try
        {
            await scenario.RunAsync();
        }
        catch (AppException) when (branch == "c")
        {
        }

        scenario.Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
        scenario.Builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Theory]
    [InlineData("a", "gate-payment")]
    [InlineData("a'", "gate-confirm")]
    [InlineData("b", "gate-approval")]
    [InlineData("c", "gate-reject")]
    [InlineData("d", "gate-payment")]
    [Trait("Catalog", "G-05")]
    public async Task WhenTheGateIsLost_Throws409WithoutPaymentUsageEventOrEmail(string branch, string gate)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch), GateWins = false };
        scenario.UsePromotion(DiscountTypeEnum.FIXED, 10_000m);

        var exception = await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync());

        // Mọi nhánh kể cả c đều 409 (không phải BOOKING_REJECTED)
        exception.ShouldBeAppException(409, ErrorCodes.Conflict, "Đơn đặt sân vừa được nhân viên xử lý, vui lòng xem lịch sử đặt sân");
        Assert.Equal(new[] { "booking", "invoice", "lock", "clear", "decide", gate }, scenario.Steps);
        var b = scenario.Builder;
        b.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        b.VnPayService.Verify(x => x.CreatePaymentUrl(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        b.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Empty(b.Broadcast.Invocations);
        Assert.Empty(b.Email.Sent);
        b.BookingRepository.Verify(x => x.GetBookingStatusAsync(scenario.BookingId), Times.Once);
        var error = Assert.Single(scenario.Logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(scenario.BookingId.ToString(), error.Message);
        Assert.Contains(DecisionFor(branch)(200_000m).Branch.ToString(), error.Message);
        Assert.Contains(nameof(BookingStatus.CANCELLED), error.Message);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a'")]
    [InlineData("b")]
    [InlineData("d")]
    public async Task SuccessfulBranches_PublishBookingCreatedOnceAfterTransaction2(string branch)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch) };

        await scenario.RunAsync();

        scenario.Builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            SignalREvents.BookingCreated, It.Is<BookingNotificationDto>(n => n.BookingId == scenario.BookingId),
            It.IsAny<Booking>(), true), Times.Once);
        Assert.True(scenario.IndexOf("broadcast") > scenario.IndexOf("decide"));
    }

    // ───────────────────────── Sự kiện SignalR sau transaction 2 (Task 3, R-7, C2) ─────────────────────────

    public static TheoryData<string, string[]> EventSequences => new()
    {
        { "a", ["broadcast"] },
        { "d", ["broadcast"] },
        { "a'", ["broadcast"] },
        { "b", ["broadcast", "pending-approval", "queue-changed"] },
        { "c", ["broadcast-staff"] },
    };

    [Theory]
    [MemberData(nameof(EventSequences))]
    public async Task Events_AreTheRightOnesInOrder_AndAllComeAfterTransaction2(string branch, string[] expected)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch) };

        try
        {
            await scenario.RunAsync();
        }
        catch (AppException) when (branch == "c")
        {
        }

        string[] eventSteps = ["broadcast", "broadcast-staff", "pending-approval", "queue-changed"];
        Assert.Equal(expected, scenario.Steps.Where(eventSteps.Contains));
        var lastWrite = scenario.Steps.FindLastIndex(step => step.StartsWith("gate-", StringComparison.Ordinal) || step is "payment" or "promotion+1");
        Assert.All(expected, e => Assert.True(scenario.IndexOf(e) > lastWrite, $"{e} phát trước khi transaction 2 xong"));
    }

    public static TheoryData<string, string, string> CreatedMessages => new()
    {
        { "a", "PENDING", "Booking #BK-ONLINE của Khách Online đã được tạo (Chờ thanh toán)." },
        { "d", "PENDING", "Booking #BK-ONLINE của Khách Online đã được tạo (Chờ thanh toán)." },
        { "a'", "CONFIRMED", "Booking #BK-ONLINE của Khách Online đã được tạo (Đã xác nhận, thanh toán tại quầy)." },
        { "b", "PENDING_APPROVAL", "Booking #BK-ONLINE của Khách Online đã được tạo (Chờ duyệt cọc)." },
    };

    [Theory]
    [MemberData(nameof(CreatedMessages))]
    [Trait("Catalog", "R-7")]
    public async Task BookingCreated_CarriesTheFinalStatusAndAMatchingMessage(string branch, string status, string message)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch) };

        await scenario.RunAsync();

        var (name, payload) = Assert.Single(scenario.BookingEvents);
        Assert.Equal(SignalREvents.BookingCreated, name);
        Assert.Equal(status, payload.Status);
        Assert.Equal(message, payload.Message);
        Assert.Equal(scenario.BookingId, payload.BookingId);
        Assert.Equal(scenario.Customer.Id, payload.CustomerId);
        Assert.Equal(scenario.Court.BranchId, payload.BranchId);
        Assert.Equal("Chi nhánh 1", payload.BranchName);
        Assert.Equal([scenario.Court.Id.ToString()], payload.CourtIds);
        Assert.Equal(NowUtc, payload.Timestamp);
        scenario.Builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            SignalREvents.BookingCreated, It.IsAny<BookingNotificationDto>(), It.Is<Booking>(bk => bk.Id == scenario.BookingId), true),
            Times.Once);
    }

    [Fact]
    public async Task BranchB_NotifiesStaffOfTheNewPendingApproval_AndTheQueue()
    {
        var scenario = new Scenario { Decision = _ => NeedApproval() };

        await scenario.RunAsync();

        var pending = Assert.Single(scenario.PendingApprovalEvents);
        Assert.Equal(scenario.BookingId, pending.BookingId);
        Assert.Equal("BK-ONLINE", pending.BookingCode);
        Assert.Equal(scenario.Court.BranchId, pending.BranchId);
        Assert.Equal("Chi nhánh 1", pending.BranchName);
        Assert.Equal("Khách Online", pending.CustomerName);
        Assert.Equal([scenario.Court.Id.ToString()], pending.CourtIds);
        Assert.Equal("PENDING_APPROVAL", pending.Status);
        Assert.Equal(ApprovalExpiresAtUtc, pending.ApprovalExpiresAt);
        Assert.Equal("Booking #BK-ONLINE của Khách Online đang chờ duyệt cọc.", pending.Message);
        Assert.Equal(NowUtc, pending.Timestamp);

        var queue = Assert.Single(scenario.QueueEvents);
        Assert.Equal((scenario.Court.BranchId, scenario.BookingId, "ADDED", "NEW", NowUtc),
            (queue.BranchId, queue.BookingId, queue.Change, queue.Reason, queue.Timestamp));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a'")]
    [InlineData("d")]
    public async Task OnlyBranchB_TouchesTheApprovalQueue(string branch)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch) };

        await scenario.RunAsync();

        Assert.Empty(scenario.PendingApprovalEvents);
        Assert.Empty(scenario.QueueEvents);
        Assert.Empty(scenario.StaffOnlyEvents);
    }

    [Fact]
    [Trait("Catalog", "C2")]
    public async Task BranchC_TellsStaffAndTheTimeGridThatTheSlotIsFree_ButNotTheCustomer()
    {
        var scenario = new Scenario { Decision = _ => Rejected() };

        (await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync())).ShouldBeAppException(400, ErrorCodes.BookingRejected);

        var (name, payload) = Assert.Single(scenario.StaffOnlyEvents);
        Assert.Equal(SignalREvents.BookingCancelled, name);
        Assert.Equal("CANCELLED", payload.Status);
        Assert.Equal("Booking #BK-ONLINE của Khách Online không được nhận theo chính sách, slot đã được giải phóng.", payload.Message);
        // Hàm phát gửi vào user_{id} không được gọi; lưới giờ được báo vì slot trống lại
        Assert.Empty(scenario.BookingEvents);
        scenario.Builder.Broadcast.Verify(x => x.BroadcastBookingEventToStaffAsync(
            SignalREvents.BookingCancelled, It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), true), Times.Once);
        Assert.Empty(scenario.PendingApprovalEvents);
        Assert.Empty(scenario.QueueEvents);
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData("b", false)]
    [InlineData("c", true)]
    public async Task WhenSignalRFails_TheBookingResultIsUnchanged(string branch, bool rejected)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch) };
        var failure = new InvalidOperationException("SignalR lỗi");
        scenario.Builder.Broadcast.Setup(x => x.BroadcastBookingEventAsync(
                It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
            .ThrowsAsync(failure);
        scenario.Builder.Broadcast.Setup(x => x.BroadcastBookingEventToStaffAsync(
                It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
            .ThrowsAsync(failure);

        if (rejected)
            (await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync())).ShouldBeAppException(400, ErrorCodes.BookingRejected);
        else
            Assert.Equal(scenario.BookingId, (await scenario.RunAsync()).BookingId);

        Assert.Contains(scenario.Logger.Entries, e => e.Level == LogLevel.Error && e.Exception == failure);
    }

    // ───────────────────────── Response theo nhánh (Task 2, BE-UT-07) ─────────────────────────

    public static TheoryData<string, string, decimal?, decimal?, decimal?, string?> ResponseCases => new()
    {
        // nhánh, status, depositRate, depositAmount, remainingAmount, depositSource (finalTotal 200.000)
        { "a", "PENDING", 0.5m, 100_000m, 100_000m, "TADSS" },
        { "d", "PENDING", 1m, 200_000m, 0m, "FULL_PAYMENT_FALLBACK" },
        { "a'", "CONFIRMED", 0m, 0m, 200_000m, "TADSS" },
        { "b", "PENDING_APPROVAL", null, null, null, null },
    };

    [Theory]
    [MemberData(nameof(ResponseCases))]
    [Trait("Catalog", "BE-UT-07")]
    public async Task Response_CarriesTheFinalStatusAndDepositFields(
        string branch, string status, decimal? rate, decimal? amount, decimal? remaining, string? source)
    {
        var scenario = new Scenario { Decision = DecisionFor(branch) };

        var result = await scenario.RunAsync();

        Assert.Equal(scenario.BookingId, result.BookingId);
        Assert.Equal("BK-ONLINE", result.BookingCode);
        Assert.Equal(status, result.Status);
        Assert.Equal(200_000m, result.FinalTotal);
        Assert.Equal(rate, result.DepositRate);
        Assert.Equal(amount, result.DepositAmount);
        Assert.Equal(remaining, result.RemainingAmount);
        Assert.Equal(source, result.DepositSource);
        Assert.Equal(branch == "b" ? ApprovalExpiresAtUtc : null, result.ApprovalExpiresAt);
        // Chỉ nhánh a/d có link và hạn thanh toán
        Assert.Equal(status == "PENDING", result.PaymentUrl != null);
        Assert.Equal(status == "PENDING", result.ExpiresAt != null);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-07")]
    public async Task Response_ForSmallOrderWithFlagOff_IsConfirmedFallbackWithTheWholeTotalRemaining()
    {
        var scenario = new Scenario(pricePerCourt: 3_000m);

        var result = await scenario.RunAsync();

        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal((0m, 0m, 3_000m, "FULL_PAYMENT_FALLBACK"),
            (result.DepositRate, result.DepositAmount, result.RemainingAmount, result.DepositSource));
        Assert.Null(result.ApprovalExpiresAt);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-07")]
    public async Task Response_UsesTheDecisionNumbersAsIs()
    {
        // BE không nhân lại rate × finalTotal: 0.3333 × 200.000 = 66.660 nhưng TADSS đã trả 66.667
        var scenario = new Scenario { Decision = _ => TadssDeposit(0.3333m, 66_667m) };

        var result = await scenario.RunAsync();

        Assert.Equal(0.3333m, result.DepositRate);
        Assert.Equal(66_667m, result.DepositAmount);
        Assert.Equal(133_333m, result.RemainingAmount);
    }

    // ───────────────────────── Giới hạn lượt khuyến mãi không điều kiện (lỗi có sẵn, sửa ở Task 4) ─────────────────────────

    [Theory]
    [TestCategory(TestCategories.Promotion)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(1, 5)]
    public async Task PromotionWithoutConditions_WhenUsageLimitIsReached_IsRejectedBeforeWriting(int limit, int used)
    {
        // Trước đây khuyến mãi không có điều kiện bỏ qua bộ kiểm lượt (trả sớm) nên dùng được vô hạn
        var scenario = new Scenario();
        var promotion = scenario.UsePromotion(DiscountTypeEnum.PERCENT, 10m);
        promotion.UsageLimit = limit;
        promotion.UsedCount = used;

        var exception = await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync());

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "Mã khuyến mãi đã hết lượt sử dụng");
        Assert.Null(scenario.CreatedBooking);
        Assert.DoesNotContain("decide", scenario.Steps);
    }

    [Fact]
    [TestCategory(TestCategories.Promotion)]
    public async Task PromotionWithoutConditions_WhenTheCustomerUsedItUp_IsRejectedBeforeWriting()
    {
        var scenario = new Scenario();
        var promotion = scenario.UsePromotion(DiscountTypeEnum.PERCENT, 10m);
        promotion.UsagePerUserLimit = 1;
        scenario.Builder.PromotionRepository.Setup(x => x.GetUserUsageCountAsync(promotion.Id, scenario.Customer.Id)).ReturnsAsync(1);

        var exception = await Assert.ThrowsAsync<AppException>(() => scenario.RunAsync());

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "Bạn đã sử dụng hết lượt áp dụng mã khuyến mãi này");
        Assert.Null(scenario.CreatedBooking);
    }

    [Fact]
    [TestCategory(TestCategories.Promotion)]
    public async Task PromotionWithoutConditions_UnderItsLimits_StillApplies()
    {
        var scenario = new Scenario();
        var promotion = scenario.UsePromotion(DiscountTypeEnum.PERCENT, 10m);
        promotion.UsageLimit = 5;
        promotion.UsedCount = 4;
        promotion.UsagePerUserLimit = 2;
        scenario.Builder.PromotionRepository.Setup(x => x.GetUserUsageCountAsync(promotion.Id, scenario.Customer.Id)).ReturnsAsync(1);

        var result = await scenario.RunAsync();

        Assert.Equal(180_000m, result.FinalTotal);
    }
}
