using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Data;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.AccessControl;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Integration.Http;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>Dựng <see cref="BookingApprovalService"/> với phụ thuộc giả; một chỗ cho mọi unit test của luồng duyệt.</summary>
internal sealed class BookingApprovalServiceTestBuilder
{
    /// <summary>09:30 giờ VN ngày 18/11/2026.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 18, 2, 30, 0, TimeSpan.Zero);

    public Mock<IBookingRepository> BookingRepository { get; } = new();
    public Mock<IUserBranchRepository> UserBranchRepository { get; } = new();
    public Mock<IUserRepository> UserRepository { get; } = new();
    public Mock<IBranchScopeResolver> BranchScope { get; } = new();
    public Mock<ITadssClient> Tadss { get; } = new();
    public Mock<IPromotionRepository> PromotionRepository { get; } = new();
    public Mock<IUnitOfWork> UnitOfWork { get; } = new();
    public Mock<ISlotInterestNotificationService> SlotInterestNotifier { get; } = new();
    public Mock<IBroadcastService> Broadcast { get; } = new();
    public TadssSettings TadssSettings { get; } = new() { ApprovedPaymentWindowMinutes = 15 };
    public BookingPaymentSettings PaymentSettings { get; } = new();
    public FakeTimeProvider Time { get; } = new(DefaultNow);
    public ListLogger<BookingApprovalService> Logger { get; } = new();
    public ListLogger<BookingNotificationService> NotificationLogger { get; } = new();
    public RecordingEmailService Email { get; } = new(TestConfigurationFactory.Create());

    public BookingApprovalService Build()
    {
        var tadssSettings = new Mock<IOptionsMonitor<TadssSettings>>();
        tadssSettings.Setup(x => x.CurrentValue).Returns(TadssSettings);
        var paymentSettings = new Mock<IOptionsMonitor<BookingPaymentSettings>>();
        paymentSettings.Setup(x => x.CurrentValue).Returns(PaymentSettings);

        return new BookingApprovalService(
            BookingRepository.Object,
            UserBranchRepository.Object,
            UserRepository.Object,
            BranchScope.Object,
            Tadss.Object,
            new PromotionEngineService(PromotionRepository.Object),
            UnitOfWork.Object,
            new BookingNotificationService(
                Broadcast.Object, Email, SlotInterestNotifier.Object, TestConfigurationFactory.Create(), Time, paymentSettings.Object,
                NotificationLogger),
            tadssSettings.Object,
            paymentSettings.Object,
            Time,
            Logger);
    }
}
