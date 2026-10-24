using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Integration.Http;

namespace SmashCourt_BE.Tests.TestData;

internal sealed class BookingCancellationServiceTestBuilder
{
    /// <summary>"Bây giờ" mặc định: 20/11/2026 17:00 giờ VN.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Chính sách hủy mẫu: trước 24 giờ hoàn 100 %, trước 12 giờ 50 %, trước 2 giờ 20 %.</summary>
    public static readonly CancelPolicy[] Policies =
    [
        new() { HoursBefore = 24, RefundPercent = 100 },
        new() { HoursBefore = 12, RefundPercent = 50 },
        new() { HoursBefore = 2, RefundPercent = 20 }
    ];

    public Mock<IBookingRepository> BookingRepository { get; } = new();
    public Mock<ISlotLockRepository> SlotLockRepository { get; } = new();
    public Mock<ICourtRepository> CourtRepository { get; } = new();
    public Mock<IPaymentRepository> PaymentRepository { get; } = new();
    public Mock<IRefundRepository> RefundRepository { get; } = new();
    public Mock<IPromotionRepository> PromotionRepository { get; } = new();
    public Mock<ICancelPolicyRepository> CancelPolicyRepository { get; } = new();
    public Mock<IUserBranchRepository> UserBranchRepository { get; } = new();
    public Mock<ISlotInterestRepository> SlotInterestRepository { get; } = new();
    public Mock<IBroadcastService> Broadcast { get; } = new();
    public FakeTimeProvider Time { get; } = new(DefaultNow);
    public IConfiguration Configuration { get; } = TestConfigurationFactory.Create();
    public ListLogger<BookingNotificationService> NotificationLogger { get; } = new();

    /// <summary>Ghi lại thư thay vì gửi SMTP.</summary>
    public RecordingEmailService Email { get; }

    public BookingCancellationServiceTestBuilder()
    {
        Email = new RecordingEmailService(Configuration);
        // Mặc định: mọi câu cập nhật có điều kiện đều thắng, không có đơn khác giữ sân, không ai chờ slot
        BookingRepository.Setup(x => x.CancelWithStatusCheckAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<BookingStatus>>(), It.IsAny<BookingStatus>(),
                It.IsAny<CancelSourceEnum>(), It.IsAny<BookingCancelReason>(), It.IsAny<Guid?>(), It.IsAny<DateTime>()))
            .ReturnsAsync(1);
        BookingRepository.Setup(x => x.TryConsumeTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync(true);
        BookingRepository.Setup(x => x.GetActiveByCourtAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>()))
            .ReturnsAsync([]);
        SlotInterestRepository.Setup(x => x.GetOverlappingSlotInterestsAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync([]);
        CancelPolicyRepository.Setup(x => x.GetAllAsync()).ReturnsAsync(Policies);
    }

    public BookingCancellationService Build()
    {
        var slotInterestNotifier = new SlotInterestNotificationService(
            SlotInterestRepository.Object, Email, Configuration, Mock.Of<ILogger<SlotInterestNotificationService>>());
        var notifications = new BookingNotificationService(
            Broadcast.Object, Email, slotInterestNotifier, Configuration, Time, NotificationLogger);

        return new BookingCancellationService(
            BookingRepository.Object,
            SlotLockRepository.Object,
            CourtRepository.Object,
            PaymentRepository.Object,
            RefundRepository.Object,
            PromotionRepository.Object,
            CancelPolicyRepository.Object,
            UserBranchRepository.Object,
            notifications,
            Time,
            Mock.Of<ILogger<BookingCancellationService>>());
    }
}
