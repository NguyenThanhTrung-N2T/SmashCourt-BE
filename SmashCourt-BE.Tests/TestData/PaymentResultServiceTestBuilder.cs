using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Integration.Http;

namespace SmashCourt_BE.Tests.TestData;

internal sealed class PaymentResultServiceTestBuilder
{
    /// <summary>"Bây giờ" mặc định: 20/11/2026 09:00 giờ VN.</summary>
    public static readonly DateTime DefaultNow = new(2026, 11, 20, 2, 0, 0, DateTimeKind.Utc);

    public Mock<IPaymentRepository> Payments { get; } = new();
    public Mock<IBookingRepository> Bookings { get; } = new();
    public Mock<IInvoiceRepository> Invoices { get; } = new();
    public Mock<ISlotLockRepository> SlotLocks { get; } = new();
    public Mock<IRefundRepository> Refunds { get; } = new();
    public Mock<IPromotionRepository> Promotions { get; } = new();
    public Mock<IBroadcastService> Broadcast { get; } = new();
    public BookingPaymentSettings Settings { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(DefaultNow));
    public IConfiguration Configuration { get; } = TestConfigurationFactory.Create();
    public ListLogger<PaymentResultService> Logger { get; } = new();
    public ListLogger<BookingNotificationService> NotificationLogger { get; } = new();

    /// <summary>Ghi lại thư thay vì gửi SMTP.</summary>
    public RecordingEmailService Email { get; }

    public PaymentResultServiceTestBuilder()
    {
        Email = new RecordingEmailService(Configuration);
        // Mặc định mọi câu cập nhật có điều kiện đều thắng
        Payments.Setup(x => x.TryMarkSuccessAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).ReturnsAsync(true);
        Payments.Setup(x => x.TryFailPendingAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).ReturnsAsync(true);
        Bookings.Setup(x => x.AtomicUpdatePaymentSuccessAsync(
                It.IsAny<Guid>(), BookingStatus.PENDING, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(1);
        Bookings.Setup(x => x.ShortenPaymentWindowAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(true);
        Bookings.Setup(x => x.MarkPendingRefundAsync(It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<DateTime>()))
            .ReturnsAsync(true);
    }

    public PaymentResultService Build()
    {
        var settings = new Mock<IOptionsMonitor<BookingPaymentSettings>>();
        settings.Setup(x => x.CurrentValue).Returns(Settings);
        var slotInterestNotifier = new SlotInterestNotificationService(
            Mock.Of<ISlotInterestRepository>(), Email, Configuration, Mock.Of<ILogger<SlotInterestNotificationService>>());
        var notifications = new BookingNotificationService(
            Broadcast.Object, Email, slotInterestNotifier, Configuration, Time, settings.Object, NotificationLogger);

        return new PaymentResultService(
            Payments.Object,
            Bookings.Object,
            Invoices.Object,
            SlotLocks.Object,
            Refunds.Object,
            new PromotionEngineService(Promotions.Object),
            notifications,
            settings.Object,
            Time,
            Logger);
    }
}
