using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.TestData;

internal sealed class BookingServiceTestBuilder
{
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
    public Mock<IVnPayService> VnPayService { get; } = new();
    public Mock<ICodeGeneratorService> CodeGeneratorService { get; } = new();
    public Mock<ISlotInterestRepository> SlotInterestRepository { get; } = new();
    public Mock<IUnitOfWork> UnitOfWork { get; } = new();
    public Mock<IBroadcastService> Broadcast { get; } = new();
    public IConfiguration Configuration { get; set; } = TestConfigurationFactory.Create();
    public ILogger<BookingService> Logger { get; set; } = Mock.Of<ILogger<BookingService>>();

    public BookingServiceTestBuilder()
    {
        BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<BookingStatus>()))
            .ReturnsAsync(1);
    }

    public BookingService Build()
    {
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
            CancelPolicyRepository.Object,
            BranchServiceRepository.Object,
            CourtRepository.Object,
            UserBranchRepository.Object,
            UserRepository.Object,
            TimeSlotRepository.Object,
            VnPayService.Object,
            new EmailService(Configuration),
            CodeGeneratorService.Object,
            SlotInterestRepository.Object,
            UnitOfWork.Object,
            Logger,
            Configuration,
            Broadcast.Object);
    }
}
