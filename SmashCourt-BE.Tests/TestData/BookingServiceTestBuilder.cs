using SmashCourt_BE.Services.Deposit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Integration.Http;

namespace SmashCourt_BE.Tests.TestData;

internal sealed class BookingServiceTestBuilder
{
    /// <summary>"Bây giờ" mặc định của luồng đặt online: 15/06/2026 08:00 giờ VN, trước khung 18:00 của dữ liệu mẫu.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 6, 15, 1, 0, 0, TimeSpan.Zero);

    public Mock<IBookingRepository> BookingRepository { get; } = new();
    public Mock<ISlotLockRepository> SlotLockRepository { get; } = new();
    public Mock<IInvoiceRepository> InvoiceRepository { get; } = new();
    public Mock<IPaymentRepository> PaymentRepository { get; } = new();
    public Mock<IRefundRepository> RefundRepository { get; } = new();
    public Mock<IBranchPriceService> PriceService { get; } = new();
    public Mock<IPromotionRepository> PromotionRepository { get; } = new();
    public Mock<ICustomerLoyaltyRepository> LoyaltyRepository { get; } = new();
    public Mock<ILoyaltyTierRepository> LoyaltyTierRepository { get; } = new();
    public Mock<ILoyaltyTransactionRepository> LoyaltyTransactionRepository { get; } = new();
    public Mock<ICancelPolicyRepository> CancelPolicyRepository { get; } = new();
    public Mock<IBranchServiceRepository> BranchServiceRepository { get; } = new();
    public Mock<ICourtRepository> CourtRepository { get; } = new();
    public Mock<IUserBranchRepository> UserBranchRepository { get; } = new();
    public Mock<IUserRepository> UserRepository { get; } = new();
    public Mock<ITimeSlotRepository> TimeSlotRepository { get; } = new();
    public Mock<IPaymentGateway> PaymentGateway { get; } = new();
    public Mock<ICodeGeneratorService> CodeGeneratorService { get; } = new();
    public Mock<ISlotInterestRepository> SlotInterestRepository { get; } = new();
    public Mock<IUnitOfWork> UnitOfWork { get; } = new();
    public Mock<IBroadcastService> Broadcast { get; } = new();
    public Mock<IBookingDepositService> DepositService { get; } = new();
    public BookingPaymentSettings PaymentSettings { get; } = new();
    public FakeTimeProvider Time { get; } = new(DefaultNow);
    public IConfiguration Configuration { get; set; } = TestConfigurationFactory.Create();
    public ILogger<BookingService> Logger { get; set; } = Mock.Of<ILogger<BookingService>>();
    public ILogger<BookingNotificationService> NotificationLogger { get; set; } = Mock.Of<ILogger<BookingNotificationService>>();

    /// <summary>Ghi lại thư thay vì gửi SMTP (không có SMTP trong unit test).</summary>
    public RecordingEmailService Email { get; }

    public BookingServiceTestBuilder()
    {
        Email = new RecordingEmailService(Configuration);
        PaymentGateway.Setup(x => x.Method).Returns(PaymentTxMethod.VNPAY);
        BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<BookingStatus>()))
            .ReturnsAsync(1);
        BookingRepository.Setup(x => x.CheckInAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()))
            .ReturnsAsync(true);
        BookingRepository.Setup(x => x.TryLockForServiceChangeAsync(It.IsAny<Guid>(), It.IsAny<DateTime>()))
            .ReturnsAsync(true);
        // Mặc định như cờ EnableTadssDecision tắt: dự phòng 100 % (d), hoặc a′ khi đơn dưới mức tối thiểu
        DepositService.Setup(x => x.DecideAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, decimal finalTotal, DateTime playStartUtc, CancellationToken _) =>
                DepositDecisionResolver.Resolve(null, finalTotal, PaymentSettings.MinOnlinePaymentAmount, Time.GetUtcNow().UtcDateTime, playStartUtc));
    }

    public BookingService Build()
    {
        var paymentSettings = new Mock<IOptionsMonitor<BookingPaymentSettings>>();
        paymentSettings.Setup(x => x.CurrentValue).Returns(PaymentSettings);
        var slotInterestNotifier = new SlotInterestNotificationService(
            SlotInterestRepository.Object, Email, Configuration, Mock.Of<ILogger<SlotInterestNotificationService>>());
        var notifications = new BookingNotificationService(
            Broadcast.Object, Email, slotInterestNotifier, Configuration, Time, paymentSettings.Object, NotificationLogger);

        return new BookingService(
            BookingRepository.Object,
            SlotLockRepository.Object,
            InvoiceRepository.Object,
            PaymentRepository.Object,
            RefundRepository.Object,
            PriceService.Object,
            PromotionRepository.Object,
            new PromotionEngineService(PromotionRepository.Object),
            LoyaltyRepository.Object,
            LoyaltyTierRepository.Object,
            LoyaltyTransactionRepository.Object,
            BranchServiceRepository.Object,
            CourtRepository.Object,
            UserBranchRepository.Object,
            UserRepository.Object,
            TimeSlotRepository.Object,
            new OnlinePaymentStarter(new PaymentGatewayResolver([PaymentGateway.Object]), PaymentRepository.Object),
            Email,
            CodeGeneratorService.Object,
            SlotInterestRepository.Object,
            notifications,
            UnitOfWork.Object,
            Logger,
            Configuration,
            Broadcast.Object,
            DepositService.Object,
            paymentSettings.Object,
            Time);
    }
}
