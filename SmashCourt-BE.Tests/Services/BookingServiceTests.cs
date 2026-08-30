using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

[TestCategory(TestCategories.Booking)]
public class BookingServiceTests
{
    [Fact]
    public async Task GetByIdAsync_WhenBookingDoesNotExist_ThrowsNotFound()
    {
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Booking?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().GetByIdAsync(
            Guid.NewGuid(), Guid.NewGuid(), UserRole.CUSTOMER.ToString()));

        exception.ShouldBeAppException(404, ErrorCodes.NotFound);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCustomerAccessesOtherCustomerBooking_ThrowsForbidden()
    {
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        var builder = new BookingServiceTestBuilder();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new Booking { CustomerId = otherCustomerId, BranchId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().GetByIdAsync(
            Guid.NewGuid(), customerId, UserRole.CUSTOMER.ToString()));

        exception.ShouldBeAppException(403, ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenGuestContactIsIncomplete_ThrowsBadRequestWithoutLoadingCourts()
    {
        var builder = new BookingServiceTestBuilder();
        var dto = CreateOnlineBookingDto();
        dto.GuestName = null;

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateOnlineAsync(dto, null));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "đầy đủ");
        builder.CourtRepository.Verify(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenNoCourtIsSelected_ThrowsBadRequest()
    {
        var builder = new BookingServiceTestBuilder();
        var dto = CreateOnlineBookingDto();
        dto.Courts.Clear();

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateOnlineAsync(dto, Guid.NewGuid()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "ít nhất 1 sân");
        builder.CourtRepository.Verify(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()), Times.Never);
        builder.BookingRepository.Verify(x => x.CreateAsync(It.IsAny<Booking>()), Times.Never);
        builder.SlotLockRepository.Verify(x => x.DeleteExpiredByBranchAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenCourtDoesNotExist_ThrowsNotFoundWithoutCreatingBooking()
    {
        var builder = new BookingServiceTestBuilder();
        var dto = CreateOnlineBookingDto(Guid.NewGuid());
        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([]);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateOnlineAsync(dto, Guid.NewGuid()));

        exception.ShouldBeAppException(404, ErrorCodes.NotFound);
        builder.BookingRepository.Verify(x => x.CreateAsync(It.IsAny<Booking>()), Times.Never);
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenCourtsBelongToDifferentBranches_ThrowsBadRequest()
    {
        var builder = new BookingServiceTestBuilder();
        var firstCourt = TestDataFactory.CreateCourt();
        var secondCourt = TestDataFactory.CreateCourt();
        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([firstCourt, secondCourt]);
        var dto = CreateOnlineBookingDto(firstCourt.Id, secondCourt.Id);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateOnlineAsync(dto, Guid.NewGuid()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "cùng 1 chi nhánh");
        builder.SlotLockRepository.Verify(x => x.DeleteExpiredByBranchAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenCourtIsSuspended_ThrowsBadRequest()
    {
        var builder = new BookingServiceTestBuilder();
        var court = TestDataFactory.CreateCourt(status: CourtStatus.SUSPENDED);
        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([court]);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateOnlineAsync(
            CreateOnlineBookingDto(court.Id), Guid.NewGuid()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "tạm ngưng");
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenSlotOverlapsExistingBooking_RegistersInterestAndThrowsUnavailable()
    {
        var builder = new BookingServiceTestBuilder();
        var court = TestDataFactory.CreateCourt();
        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([court]);
        builder.BookingRepository.Setup(x => x.HasOverlapAsync(
                court.Id, It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync(true);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateOnlineAsync(
            CreateOnlineBookingDto(court.Id), Guid.NewGuid()));

        exception.ShouldBeAppException(400, ErrorCodes.SlotUnavailableNotifyRegistered);
        builder.SlotInterestRepository.Verify(x => x.CreateAsync(It.Is<SlotInterest>(interest =>
            interest.CourtId == court.Id && interest.Email == "guest@example.com")), Times.Once);
        builder.BookingRepository.Verify(x => x.CreateAsync(It.IsAny<Booking>()), Times.Never);
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenSlotIsLocked_ThrowsUnavailableBeforePricing()
    {
        var builder = new BookingServiceTestBuilder();
        var court = TestDataFactory.CreateCourt();
        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([court]);
        builder.SlotLockRepository.Setup(x => x.GetByCourtAndTimeAsync(
                court.Id, It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync(new SlotLock { CourtId = court.Id });

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateOnlineAsync(
            CreateOnlineBookingDto(court.Id), Guid.NewGuid()));

        exception.ShouldBeAppException(400, ErrorCodes.SlotUnavailableNotifyRegistered);
        builder.PriceService.Verify(x => x.CalculateForBookingAsync(
            It.IsAny<Guid?>(), It.IsAny<SmashCourt_BE.DTOs.PriceConfig.CalculatePriceDto>()), Times.Never);
    }

    [Fact]
    public async Task CreateWalkInAsync_WhenNoCourtIsSelected_ThrowsBadRequest()
    {
        var builder = new BookingServiceTestBuilder();
        var dto = new CreateWalkInBookingDto
        {
            BookingDate = TestConstants.StandardDateTime
        };

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateWalkInAsync(dto, Guid.NewGuid()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "ít nhất 1 sân");
    }

    [Fact]
    public async Task CreateWalkInAsync_WhenStaffUserDoesNotExist_ThrowsNotFound()
    {
        var builder = new BookingServiceTestBuilder();
        var court = TestDataFactory.CreateCourt();
        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([court]);
        builder.UserRepository.Setup(x => x.GetUserByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((User?)null);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateWalkInAsync(
            CreateWalkInBookingDto(court.Id), Guid.NewGuid()));

        exception.ShouldBeAppException(404, ErrorCodes.NotFound, "người dùng");
    }

    [Fact]
    public async Task CreateWalkInAsync_WhenStaffIsOutsideCourtBranch_ThrowsForbidden()
    {
        var builder = new BookingServiceTestBuilder();
        var court = TestDataFactory.CreateCourt();
        var staff = TestDataFactory.CreateUser(role: UserRole.STAFF);
        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([court]);
        builder.UserRepository.Setup(x => x.GetUserByIdAsync(staff.Id))
            .ReturnsAsync(staff);
        builder.UserBranchRepository.Setup(x => x.IsUserInBranchAsync(staff.Id, court.BranchId))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CreateWalkInAsync(
            CreateWalkInBookingDto(court.Id), staff.Id));

        exception.ShouldBeAppException(403, ErrorCodes.Forbidden, "chi nhánh");
    }

    [Fact]
    public async Task CreateOnlineAsync_WhenTwoCourtsHaveLoyaltyDiscount_CreatesInvoiceLocksAndPayment()
    {
        var builder = new BookingServiceTestBuilder();
        var customerId = Guid.NewGuid();
        var firstCourt = TestDataFactory.CreateCourt();
        var secondCourt = TestDataFactory.CreateCourt(branchId: firstCourt.BranchId);
        var bookingId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            BookingId = bookingId,
            InvoiceCode = "INV-ONLINE",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        var persistedBooking = TestDataFactory.CreateBooking(customerId, firstCourt.BranchId);
        persistedBooking.Id = bookingId;
        persistedBooking.BookingCode = "BK-ONLINE";
        persistedBooking.Invoice = invoice;

        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([firstCourt, secondCourt]);
        builder.BookingRepository.Setup(x => x.HasOverlapAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync(false);
        builder.BookingRepository.Setup(x => x.CreateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking booking) =>
            {
                booking.Id = bookingId;
                return booking;
            });
        builder.BookingRepository.Setup(x => x.AddCourtAsync(It.IsAny<BookingCourt>()))
            .ReturnsAsync((BookingCourt court) =>
            {
                court.Id = Guid.NewGuid();
                return court;
            });
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(bookingId))
            .ReturnsAsync(persistedBooking);
        builder.PriceService.Setup(x => x.CalculateForBookingAsync(
                firstCourt.BranchId, It.IsAny<CalculatePriceDto>()))
            .ReturnsAsync(TestDataFactory.CreatePriceResult(
                [firstCourt.Id, secondCourt.Id], TestConstants.StandardCourtPrice));
        builder.LoyaltyRepository.Setup(x => x.GetByUserIdAsync(customerId))
            .ReturnsAsync(new CustomerLoyalty
            {
                UserId = customerId,
                Tier = new LoyaltyTier { DiscountRate = 10m }
            });
        builder.TimeSlotRepository.Setup(x => x.GetAllAsync())
            .ReturnsAsync([new TimeSlot
            {
                Id = Guid.NewGuid(),
                StartTime = TimeOnly.FromTimeSpan(TestConstants.EveningStartTime),
                EndTime = TimeOnly.FromTimeSpan(TestConstants.EveningEndTime),
                DayType = DayType.WEEKDAY
            }]);
        builder.InvoiceRepository.Setup(x => x.CreateAsync(It.IsAny<Invoice>()))
            .ReturnsAsync((Invoice created) =>
            {
                created.Id = invoiceId;
                created.ExpiresAt = invoice.ExpiresAt;
                return created;
            });
        builder.CodeGeneratorService.Setup(x => x.GenerateBookingCodeAsync())
            .ReturnsAsync("BK-ONLINE");
        builder.CodeGeneratorService.Setup(x => x.GenerateInvoiceCodeAsync())
            .ReturnsAsync("INV-ONLINE");
        builder.VnPayService.Setup(x => x.CreatePaymentUrl(
                bookingId.ToString(), 360_000m, It.IsAny<string>()))
            .Returns(new VnPayPaymentUrlResult { Url = "https://vnpay.test/pay", TransactionRef = "TX-ONLINE" });

        var result = await builder.Build().CreateOnlineAsync(
            CreateOnlineBookingDto(firstCourt.Id, secondCourt.Id), customerId);

        Assert.Equal(bookingId, result.BookingId);
        Assert.Equal("https://vnpay.test/pay", result.PaymentUrl);
        Assert.Equal(360_000m, result.FinalTotal);
        builder.BookingRepository.Verify(x => x.AddCourtAsync(It.IsAny<BookingCourt>()), Times.Exactly(2));
        builder.BookingRepository.Verify(x => x.AddPriceItemsAsync(It.IsAny<List<BookingPriceItem>>()), Times.Exactly(2));
        builder.SlotLockRepository.Verify(x => x.CreateAsync(It.Is<SlotLock>(lockItem =>
            lockItem.BookingId == bookingId)), Times.Exactly(2));
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.Is<Payment>(payment =>
            payment.InvoiceId == invoiceId &&
            payment.Amount == 360_000m &&
            payment.Status == PaymentTxStatus.PENDING)), Times.Once);
    }

    [Fact]
    public async Task CreateWalkInAsync_WhenPostpaidBookingSucceeds_ReturnsConfirmedBookingWithoutPayment()
    {
        var builder = new BookingServiceTestBuilder();
        var owner = TestDataFactory.CreateUser(role: UserRole.OWNER);
        var court = TestDataFactory.CreateCourt();
        var bookingId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var invoice = new Invoice
        {
            Id = invoiceId,
            BookingId = bookingId,
            InvoiceCode = "INV-WALKIN",
            CourtFee = TestConstants.StandardCourtPrice,
            FinalTotal = TestConstants.StandardCourtPrice,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        var persistedBooking = TestDataFactory.CreateBooking(
            branchId: court.BranchId,
            status: BookingStatus.CONFIRMED,
            totalAmount: TestConstants.StandardCourtPrice);
        persistedBooking.Id = bookingId;
        persistedBooking.BookingCode = "BK-WALKIN";
        persistedBooking.Source = BookingSource.WALK_IN;
        persistedBooking.Invoice = invoice;

        builder.CourtRepository.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync([court]);
        builder.UserRepository.Setup(x => x.GetUserByIdAsync(owner.Id)).ReturnsAsync(owner);
        builder.BookingRepository.Setup(x => x.HasOverlapAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync(false);
        builder.BookingRepository.Setup(x => x.CreateAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking booking) =>
            {
                booking.Id = bookingId;
                return booking;
            });
        builder.BookingRepository.Setup(x => x.AddCourtAsync(It.IsAny<BookingCourt>()))
            .ReturnsAsync((BookingCourt bookingCourt) =>
            {
                bookingCourt.Id = Guid.NewGuid();
                return bookingCourt;
            });
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(bookingId))
            .ReturnsAsync(persistedBooking);
        builder.PriceService.Setup(x => x.CalculateForBookingAsync(
                court.BranchId, It.IsAny<CalculatePriceDto>()))
            .ReturnsAsync(TestDataFactory.CreatePriceResult([court.Id]));
        builder.TimeSlotRepository.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
        builder.InvoiceRepository.Setup(x => x.CreateAsync(It.IsAny<Invoice>()))
            .ReturnsAsync((Invoice created) =>
            {
                created.Id = invoiceId;
                return created;
            });
        builder.CodeGeneratorService.Setup(x => x.GenerateBookingCodeAsync())
            .ReturnsAsync("BK-WALKIN");
        builder.CodeGeneratorService.Setup(x => x.GenerateInvoiceCodeAsync())
            .ReturnsAsync("INV-WALKIN");

        var result = await builder.Build().CreateWalkInAsync(
            CreateWalkInBookingDto(court.Id), owner.Id);

        Assert.Equal(bookingId, result.Id);
        Assert.Equal(BookingStatus.CONFIRMED.ToString(), result.Status);
        Assert.Equal(BookingSource.WALK_IN.ToString(), result.Source);
        Assert.Equal(TestConstants.StandardCourtPrice, result.FinalTotal);
        builder.InvoiceRepository.Verify(x => x.CreateAsync(It.Is<Invoice>(created =>
            created.BookingId == bookingId &&
            created.PaymentTiming == PaymentTiming.POSTPAID)), Times.Once);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        builder.SlotLockRepository.Verify(x => x.CreateAsync(It.IsAny<SlotLock>()), Times.Never);
    }

    [Fact]
    public async Task AddServiceAsync_WhenStatusChangesDuringTransaction_ThrowsBadRequestWithoutMutation()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var service = new Service { Id = Guid.NewGuid(), Name = "Water", Unit = "bottle" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.Branch = branch;
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, paymentStatus: InvoicePaymentStatus.UNPAID);
        var branchService = new SmashCourt_BE.Models.Entities.BranchService
        {
            BranchId = branch.Id,
            ServiceId = service.Id,
            Price = 30_000m,
            Service = service
        };
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.GetBookingStatusAsync(booking.Id))
            .ReturnsAsync(BookingStatus.COMPLETED);
        builder.BranchServiceRepository.Setup(x => x.GetByBranchServiceAsync(branch.Id, service.Id))
            .ReturnsAsync(branchService);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(booking.Invoice);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().AddServiceAsync(
            booking.Id, new AddBookingServiceDto { ServiceId = service.Id, Quantity = 1 },
            Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest);
        builder.BookingRepository.Verify(x => x.AddServiceAsync(It.IsAny<SmashCourt_BE.Models.Entities.BookingService>()), Times.Never);
        builder.BookingRepository.Verify(x => x.UpdateServiceQuantityAtomicAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task RemoveServiceAsync_WhenInvoiceIsPaid_ThrowsBadRequestWithoutMutation()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var serviceId = Guid.NewGuid();
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.Branch = branch;
        booking.Invoice = TestDataFactory.CreateInvoice(
            booking.Id, paymentStatus: InvoicePaymentStatus.PAID);
        booking.BookingServices.Add(new SmashCourt_BE.Models.Entities.BookingService
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            ServiceId = serviceId,
            ServiceName = "Water",
            Unit = "bottle",
            UnitPrice = 30_000m,
            Quantity = 1
        });
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.GetBookingStatusAsync(booking.Id)).ReturnsAsync(booking.Status);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(booking.Invoice);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().RemoveServiceAsync(
            booking.Id, serviceId, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest);
        builder.BookingRepository.Verify(x => x.RemoveServiceAsync(It.IsAny<SmashCourt_BE.Models.Entities.BookingService>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task AddServiceAsync_WhenServiceIsDisabled_ThrowsBadRequestWithoutMutation()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var service = new Service { Id = Guid.NewGuid(), Name = "Water", Unit = "bottle" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.Branch = branch;
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id);
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BranchServiceRepository.Setup(x => x.GetByBranchServiceAsync(branch.Id, service.Id))
            .ReturnsAsync(new SmashCourt_BE.Models.Entities.BranchService
            {
                BranchId = branch.Id,
                ServiceId = service.Id,
                Status = BranchServiceStatus.DISABLED,
                Service = service
            });

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().AddServiceAsync(
            booking.Id, new AddBookingServiceDto { ServiceId = service.Id, Quantity = 1 },
            Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "đã bị tắt");
        builder.BookingRepository.Verify(x => x.AddServiceAsync(It.IsAny<SmashCourt_BE.Models.Entities.BookingService>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task AddServiceAsync_WhenServiceAlreadyExists_IncrementsQuantityAtomically()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var service = new Service { Id = Guid.NewGuid(), Name = "Water", Unit = "bottle" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.Branch = branch;
        booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, paymentStatus: InvoicePaymentStatus.UNPAID);
        booking.BookingServices.Add(new SmashCourt_BE.Models.Entities.BookingService
        {
            Id = Guid.NewGuid(), ServiceId = service.Id, Quantity = 2, UnitPrice = 30_000m,
            ServiceName = service.Name
        });
        var branchService = new SmashCourt_BE.Models.Entities.BranchService
        {
            BranchId = branch.Id, ServiceId = service.Id, Price = 30_000m, Service = service
        };
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.GetBookingStatusAsync(booking.Id)).ReturnsAsync(booking.Status);
        builder.BranchServiceRepository.Setup(x => x.GetByBranchServiceAsync(branch.Id, service.Id)).ReturnsAsync(branchService);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(booking.Invoice);
        builder.BookingRepository.Setup(x => x.CalculateServiceFeeAsync(booking.Id)).ReturnsAsync(150_000m);

        await builder.Build().AddServiceAsync(
            booking.Id, new AddBookingServiceDto { ServiceId = service.Id, Quantity = 3 },
            Guid.NewGuid(), UserRole.OWNER.ToString());

        builder.BookingRepository.Verify(x => x.UpdateServiceQuantityAtomicAsync(
            booking.BookingServices.Single().Id, 3), Times.Once);
        builder.BookingRepository.Verify(x => x.AddServiceAsync(It.IsAny<SmashCourt_BE.Models.Entities.BookingService>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.Is<Invoice>(invoice =>
            invoice.ServiceFee == 150_000m && invoice.FinalTotal == 1_150_000m)), Times.Once);
    }

    [Fact]
    public async Task CheckInAsync_WhenWithinWindow_UpdatesBookingCourtAndBroadcasts()
    {
        var builder = new BookingServiceTestBuilder();
        var booking = CreateCheckInBooking(BookingStatus.CONFIRMED, DateTimeHelper.GetVietnamNow().AddMinutes(-5));
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);

        await builder.Build().CheckInAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
        Assert.NotNull(booking.CheckedInAt);
        Assert.Equal(CourtStatus.IN_USE, booking.BookingCourts.Single().Court.Status);
        builder.BookingRepository.Verify(x => x.UpdateAsync(booking), Times.Once);
        builder.CourtRepository.Verify(x => x.UpdateAsync(booking.BookingCourts.Single().Court), Times.Once);
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            SignalREvents.BookingCheckedIn,
            It.Is<BookingNotificationDto>(notification => notification.Status == BookingStatus.IN_PROGRESS.ToString()),
            booking, false), Times.Once);
    }

    [Fact]
    public async Task CancelByStaffAsync_WhenPendingBookingIsUnpaid_CancelsBookingAndReleasesCourt()
    {
        var builder = new BookingServiceTestBuilder();
        var staffId = Guid.NewGuid();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch", Address = "Address" };
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(
            branchId: branch.Id,
            status: BookingStatus.PENDING);
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id,
            CourtId = court.Id,
            Date = booking.BookingDate,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 30),
            Court = court
        });
        booking.Branch = branch;
        booking.Invoice = new Invoice { PaymentStatus = InvoicePaymentStatus.UNPAID };

        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);
        builder.SlotInterestRepository.Setup(x => x.GetOverlappingSlotInterestsAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync([]);

        await builder.Build().CancelByStaffAsync(booking.Id, staffId, UserRole.OWNER.ToString());

        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        Assert.Equal(staffId, booking.CancelledBy);
        Assert.Equal(CancelSourceEnum.STAFF, booking.CancelSource);
        Assert.Equal(CourtStatus.AVAILABLE, court.Status);
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(booking.Id, false), Times.Once);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(booking.Id), Times.Once);
        builder.CourtRepository.Verify(x => x.UpdateAsync(It.Is<Court>(updated =>
            updated.Id == court.Id && updated.Status == CourtStatus.AVAILABLE)), Times.Once);
        builder.BookingRepository.Verify(x => x.UpdateAsync(It.Is<Booking>(updated =>
            updated.Status == BookingStatus.CANCELLED)), Times.Once);
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            SignalREvents.BookingCancelled,
            It.IsAny<SmashCourt_BE.DTOs.SignalR.BookingNotificationDto>(),
            booking,
            true), Times.Once);
    }

    [Fact]
    public async Task CancelByStaffAsync_WhenPaidBookingIsWithinRefundWindow_CreatesRefundAndDecrementsPromotion()
    {
        var builder = new BookingServiceTestBuilder();
        var staffId = Guid.NewGuid();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch", Address = "Address" };
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(
            branchId: branch.Id,
            status: BookingStatus.PAID_ONLINE,
            totalAmount: 360_000m);
        booking.BookingDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        booking.Branch = branch;
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id,
            CourtId = court.Id,
            Date = booking.BookingDate,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 30),
            Court = court
        });
        var promotionId = Guid.NewGuid();
        booking.BookingPromotion = new BookingPromotion { PromotionId = promotionId };
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = PaymentTxStatus.SUCCESS,
            Amount = 360_000m
        };
        booking.Invoice = new Invoice
        {
            FinalTotal = 360_000m,
            PaymentStatus = InvoicePaymentStatus.PAID,
            Payments = [payment]
        };

        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);
        builder.CancelPolicyRepository.Setup(x => x.GetAllAsync())
            .ReturnsAsync([new CancelPolicy { HoursBefore = 24, RefundPercent = 50m }]);
        builder.SlotInterestRepository.Setup(x => x.GetOverlappingSlotInterestsAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync([]);

        await builder.Build().CancelByStaffAsync(booking.Id, staffId, UserRole.OWNER.ToString());

        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.Is<Refund>(refund =>
            refund.PaymentId == payment.Id &&
            refund.Amount == 180_000m &&
            refund.RefundPercent == 50m &&
            refund.Status == RefundStatus.PENDING)), Times.Once);
        builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(promotionId), Times.Once);
    }

    [Fact]
    public async Task CancelByCustomerAsync_WhenBookingIsAlreadyCancelled_IsIdempotent()
    {
        var builder = new BookingServiceTestBuilder();
        var customerId = Guid.NewGuid();
        var booking = TestDataFactory.CreateBooking(
            customerId: customerId,
            status: BookingStatus.CANCELLED);
        booking.Customer = TestDataFactory.CreateUser();

        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        await builder.Build().CancelByCustomerAsync(booking.Id, customerId);

        builder.BookingRepository.Verify(x => x.UpdateAsync(It.IsAny<Booking>()), Times.Never);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(It.IsAny<Guid>()), Times.Never);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
    }

    [Fact]
    public async Task CancelByCustomerAsync_WhenWithinRefundWindow_CreatesPendingRefund()
    {
        var builder = new BookingServiceTestBuilder();
        var customer = TestDataFactory.CreateUser();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(
            customerId: customer.Id, branchId: branch.Id, status: BookingStatus.PAID_ONLINE,
            bookingDate: DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6)));
        booking.Customer = customer;
        booking.Branch = branch;
        var payment = TestDataFactory.CreatePayment(amount: 200_000m, status: PaymentTxStatus.SUCCESS);
        booking.Invoice = TestDataFactory.CreateInvoice(
            booking.Id, finalTotal: 200_000m, paymentStatus: InvoicePaymentStatus.PAID,
            payments: [payment]);
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id, CourtId = court.Id, Date = booking.BookingDate,
            StartTime = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(6)),
            EndTime = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), Court = court, IsActive = true
        });
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.CancelPolicyRepository.Setup(x => x.GetAllAsync())
            .ReturnsAsync([new CancelPolicy { HoursBefore = 1, RefundPercent = 80m }]);
        builder.BookingRepository.Setup(x => x.GetActiveByCourtAndDateAsync(court.Id, booking.BookingDate))
            .ReturnsAsync([]);
        builder.SlotInterestRepository.Setup(x => x.GetOverlappingSlotInterestsAsync(
                court.Id, booking.BookingDate, It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync([]);

        await builder.Build().CancelByCustomerAsync(booking.Id, customer.Id);

        Assert.Equal(BookingStatus.CANCELLED_PENDING_REFUND, booking.Status);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.Is<Refund>(refund =>
            refund.PaymentId == payment.Id && refund.Amount == 160_000m &&
            refund.RefundPercent == 80m && refund.Status == RefundStatus.PENDING)), Times.Once);
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(booking.Id, false), Times.Once);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(booking.Id), Times.Once);
    }

    [Fact]
    public async Task CancelByCustomerAsync_WhenNoRefundPolicyApplies_CancelsWithoutRefund()
    {
        var builder = new BookingServiceTestBuilder();
        var customer = TestDataFactory.CreateUser();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var court = TestDataFactory.CreateCourt(branch.Id);
        var start = DateTime.UtcNow.AddMinutes(30);
        var booking = TestDataFactory.CreateBooking(
            customerId: customer.Id, branchId: branch.Id, status: BookingStatus.PAID_ONLINE,
            bookingDate: DateOnly.FromDateTime(start));
        booking.Customer = customer;
        booking.Branch = branch;
        var payment = TestDataFactory.CreatePayment(amount: 200_000m, status: PaymentTxStatus.SUCCESS);
        booking.Invoice = TestDataFactory.CreateInvoice(
            booking.Id, finalTotal: 200_000m, paymentStatus: InvoicePaymentStatus.PAID,
            payments: [payment]);
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id, CourtId = court.Id, Date = booking.BookingDate,
            StartTime = TimeOnly.FromDateTime(start), EndTime = TimeOnly.FromDateTime(start.AddHours(1)),
            Court = court, IsActive = true
        });
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.CancelPolicyRepository.Setup(x => x.GetAllAsync())
            .ReturnsAsync([new CancelPolicy { HoursBefore = 1, RefundPercent = 80m }]);
        builder.BookingRepository.Setup(x => x.GetActiveByCourtAndDateAsync(court.Id, booking.BookingDate))
            .ReturnsAsync([]);
        builder.SlotInterestRepository.Setup(x => x.GetOverlappingSlotInterestsAsync(
                court.Id, booking.BookingDate, It.IsAny<TimeOnly>(), It.IsAny<TimeOnly>()))
            .ReturnsAsync([]);

        await builder.Build().CancelByCustomerAsync(booking.Id, customer.Id);

        Assert.Equal(BookingStatus.CANCELLED, booking.Status);
        builder.RefundRepository.Verify(x => x.CreateAsync(It.IsAny<Refund>()), Times.Never);
    }

    [Fact]
    public async Task CancelByTokenAsync_WhenAtomicTokenConsumptionFails_ThrowsBadRequestWithoutUpdatingBooking()
    {
        var builder = new BookingServiceTestBuilder();
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.CONFIRMED);
        booking.CancelTokenExpiresAt = DateTime.UtcNow.AddHours(1);
        builder.BookingRepository.Setup(x => x.GetByCancelTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.TryConsumeTokenAsync(
                booking.Id, It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync(false);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            builder.Build().CancelByTokenAsync("one-time-token"));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "đã được sử dụng");
        builder.BookingRepository.Verify(x => x.TryConsumeTokenAsync(
            booking.Id, It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
        builder.BookingRepository.Verify(x => x.UpdateAsync(It.IsAny<Booking>()), Times.Never);
        builder.SlotLockRepository.Verify(x => x.DeleteByBookingIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenConditionalStatusUpdateLosesRace_ThrowsConflictWithoutPayment()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.Branch = branch;
        booking.Invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            FinalTotal = 200_000m,
            PaymentStatus = InvoicePaymentStatus.UNPAID
        };
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                booking.Id, BookingStatus.COMPLETED, BookingStatus.IN_PROGRESS))
            .ReturnsAsync(0);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            builder.Build().CheckoutAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(409, ErrorCodes.Conflict, "checkout bởi người khác");
        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task AddServiceAsync_WhenQuantityIsZero_ThrowsBadRequestBeforeLoadingBooking()
    {
        var builder = new BookingServiceTestBuilder();

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().AddServiceAsync(
            Guid.NewGuid(), new AddBookingServiceDto { ServiceId = Guid.NewGuid(), Quantity = 0 },
            Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "lớn hơn 0");
        builder.BookingRepository.Verify(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task AddServiceAsync_WhenBookingIsCompleted_ThrowsBadRequestWithoutLoadingService()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.COMPLETED);
        booking.Branch = branch;
        booking.Invoice = new Invoice { PaymentStatus = InvoicePaymentStatus.PAID };
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().AddServiceAsync(
            booking.Id, new AddBookingServiceDto { ServiceId = Guid.NewGuid(), Quantity = 1 },
            Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "trạng thái hiện tại");
        builder.BranchServiceRepository.Verify(x => x.GetByBranchServiceAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        builder.BookingRepository.Verify(x => x.AddServiceAsync(It.IsAny<SmashCourt_BE.Models.Entities.BookingService>()), Times.Never);
    }

    [Fact]
    public async Task CheckInAsync_WhenBookingHasNoCourt_ThrowsInternalErrorWithoutUpdatingBooking()
    {
        var builder = new BookingServiceTestBuilder();
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.CONFIRMED);
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CheckInAsync(
            booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(500, ErrorCodes.InternalError, "khung giờ");
        builder.BookingRepository.Verify(x => x.UpdateAsync(It.IsAny<Booking>()), Times.Never);
        builder.CourtRepository.Verify(x => x.UpdateAsync(It.IsAny<Court>()), Times.Never);
    }

    [Fact]
    public async Task CheckInAsync_WhenTooEarly_ThrowsBadRequestWithoutSideEffects()
    {
        var builder = new BookingServiceTestBuilder();
        var booking = CreateCheckInBooking(BookingStatus.CONFIRMED, DateTimeHelper.GetVietnamNow().AddHours(1));
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CheckInAsync(
            booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "Quá sớm");
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        builder.BookingRepository.Verify(x => x.UpdateAsync(It.IsAny<Booking>()), Times.Never);
        builder.CourtRepository.Verify(x => x.UpdateAsync(It.IsAny<Court>()), Times.Never);
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task CheckInAsync_WhenTooLate_ThrowsBadRequestWithoutSideEffects()
    {
        var builder = new BookingServiceTestBuilder();
        var booking = CreateCheckInBooking(BookingStatus.CONFIRMED, DateTimeHelper.GetVietnamNow().AddHours(-1));
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CheckInAsync(
            booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "quá thời gian");
        Assert.Equal(BookingStatus.CONFIRMED, booking.Status);
        builder.BookingRepository.Verify(x => x.UpdateAsync(It.IsAny<Booking>()), Times.Never);
        builder.CourtRepository.Verify(x => x.UpdateAsync(It.IsAny<Court>()), Times.Never);
        builder.Broadcast.Verify(x => x.BroadcastBookingEventAsync(
            It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RemoveServiceAsync_WhenServiceIsAlreadyMissing_IsIdempotent()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.Branch = branch;
        booking.Invoice = TestDataFactory.CreateInvoice(
            booking.Id, finalTotal: 200_000m, paymentStatus: InvoicePaymentStatus.UNPAID);
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        var result = await builder.Build().RemoveServiceAsync(
            booking.Id, Guid.NewGuid(), Guid.NewGuid(), UserRole.OWNER.ToString());

        Assert.Equal(booking.Id, result.Id);
        builder.BookingRepository.Verify(x => x.RemoveServiceAsync(
            It.IsAny<SmashCourt_BE.Models.Entities.BookingService>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task GetCancelInfoAsync_WhenTokenIsExpired_ThrowsBadRequestWithoutLoadingRefundPolicy()
    {
        var builder = new BookingServiceTestBuilder();
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.CONFIRMED);
        booking.CancelTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        builder.BookingRepository.Setup(x => x.GetByCancelTokenAsync(It.IsAny<string>()))
            .ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            builder.Build().GetCancelInfoAsync("expired-token"));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "hết hạn");
        builder.CancelPolicyRepository.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenInvoiceIsUnpaid_CollectsFullAmountAndMarksInvoicePaid()
    {
        var builder = new BookingServiceTestBuilder();
        var invoice = TestDataFactory.CreateInvoice(finalTotal: 250_000m);
        var booking = CreateCheckoutBooking(BookingStatus.IN_PROGRESS, invoice);
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                booking.Id, BookingStatus.COMPLETED, BookingStatus.IN_PROGRESS))
            .ReturnsAsync(1);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(invoice);

        await builder.Build().CheckoutAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        Assert.Equal(InvoicePaymentStatus.PAID, invoice.PaymentStatus);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.Is<Payment>(payment =>
            payment.InvoiceId == invoice.Id &&
            payment.Amount == 250_000m &&
            payment.Method == PaymentTxMethod.CASH &&
            payment.Status == PaymentTxStatus.SUCCESS)), Times.Once);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.Is<Invoice>(updated =>
            updated.PaymentStatus == InvoicePaymentStatus.PAID)), Times.Once);
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(booking.Id, false), Times.Once);
        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(
            It.Is<List<Guid>>(ids => ids.SequenceEqual(new[] { booking.BookingCourts.Single().CourtId })),
            CourtStatus.AVAILABLE,
            It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task CheckoutAsync_WhenCustomerCrossesLoyaltyTier_EarnsPointsAndUpgradesTier()
    {
        var builder = new BookingServiceTestBuilder();
        var invoice = TestDataFactory.CreateInvoice(paymentStatus: InvoicePaymentStatus.PAID);
        invoice.CourtFee = 200_000m;
        var booking = CreateCheckoutBooking(BookingStatus.IN_PROGRESS, invoice);
        var silverId = Guid.NewGuid();
        var goldId = Guid.NewGuid();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                booking.Id, BookingStatus.COMPLETED, BookingStatus.IN_PROGRESS))
            .ReturnsAsync(1);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(invoice);
        builder.LoyaltyRepository.Setup(x => x.GetByUserIdAsync(booking.CustomerId!.Value))
            .ReturnsAsync(new CustomerLoyalty { UserId = booking.CustomerId.Value, TierId = silverId });
        builder.LoyaltyRepository.Setup(x => x.AddPointsAtomicAsync(booking.CustomerId.Value, 200))
            .ReturnsAsync(5_100);
        builder.LoyaltyTierRepository.Setup(x => x.GetAllLoyaltyTiersAsync())
            .ReturnsAsync([
                new LoyaltyTier { Id = silverId, Name = "Silver", MinPoints = 1_000 },
                new LoyaltyTier { Id = goldId, Name = "Gold", MinPoints = 5_000 }
            ]);

        await builder.Build().CheckoutAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        builder.LoyaltyRepository.Verify(x => x.AddPointsAtomicAsync(
            booking.CustomerId.Value, 200), Times.Once);
        builder.LoyaltyRepository.Verify(x => x.UpdateTierAsync(
            booking.CustomerId.Value, goldId), Times.Once);
        builder.LoyaltyTransactionRepository.Verify(x => x.AddAsync(It.Is<LoyaltyTransaction>(transaction =>
            transaction.BookingId == booking.Id &&
            transaction.Points == 200 &&
            transaction.TotalPointsAfter == 5_100 &&
            transaction.Type == LoyaltyTransactionType.EARN)), Times.Once);
    }

    [Fact]
    public async Task CheckoutAsync_WhenInvoiceIsMissing_ThrowsInternalErrorWithoutSideEffects()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.IN_PROGRESS);
        booking.Branch = branch;
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CheckoutAsync(
            booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(500, ErrorCodes.InternalError, "hóa đơn");
        Assert.Equal(BookingStatus.IN_PROGRESS, booking.Status);
        builder.BookingRepository.Verify(x => x.UpdateWithStatusCheckAsync(
            It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<BookingStatus>()), Times.Never);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
        builder.BookingRepository.Verify(x => x.UpdateCourtActiveStatusAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(
            It.IsAny<List<Guid>>(), It.IsAny<CourtStatus>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenInvoiceIsAlreadyPaid_DoesNotCreatePayment()
    {
        var builder = new BookingServiceTestBuilder();
        var invoice = TestDataFactory.CreateInvoice(paymentStatus: InvoicePaymentStatus.PAID);
        var booking = CreateCheckoutBooking(BookingStatus.IN_PROGRESS, invoice);
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                booking.Id, BookingStatus.COMPLETED, BookingStatus.IN_PROGRESS))
            .ReturnsAsync(1);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(invoice);

        await builder.Build().CheckoutAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.Is<Invoice>(updated =>
            updated.PaymentStatus == InvoicePaymentStatus.PAID)), Times.Once);
    }

    [Fact]
    public async Task CheckoutAsync_WhenBookingIsAlreadyCompleted_RejectsWithoutMutation()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.COMPLETED);
        booking.Branch = branch;
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().CheckoutAsync(
            booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest);
        builder.BookingRepository.Verify(x => x.UpdateWithStatusCheckAsync(
            It.IsAny<Guid>(), It.IsAny<BookingStatus>(), It.IsAny<BookingStatus>()), Times.Never);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.IsAny<Payment>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenBookingHasMultipleCourts_UpdatesAllCourtsToAvailable()
    {
        var builder = new BookingServiceTestBuilder();
        var invoice = TestDataFactory.CreateInvoice(paymentStatus: InvoicePaymentStatus.PAID);
        var booking = CreateCheckoutBooking(BookingStatus.IN_PROGRESS, invoice);
        var secondCourt = TestDataFactory.CreateCourt(booking.BranchId);
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id,
            CourtId = secondCourt.Id,
            Date = booking.BookingDate,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 30),
            Court = secondCourt,
            IsActive = true
        });
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                booking.Id, BookingStatus.COMPLETED, BookingStatus.IN_PROGRESS))
            .ReturnsAsync(1);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(invoice);

        await builder.Build().CheckoutAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        builder.CourtRepository.Verify(x => x.BatchUpdateStatusAsync(
            It.Is<List<Guid>>(ids => ids.Count == 2 && ids.Contains(booking.BookingCourts.First().CourtId) && ids.Contains(secondCourt.Id)),
            CourtStatus.AVAILABLE,
            It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmRefundAsync_WhenBookingIsNotPendingRefund_ThrowsWithoutSideEffects()
    {
        var builder = new BookingServiceTestBuilder();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: BookingStatus.CONFIRMED);
        booking.Branch = branch;
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id))
            .ReturnsAsync(booking);

        var exception = await Assert.ThrowsAsync<AppException>(() => builder.Build().ConfirmRefundAsync(
            booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString()));

        exception.ShouldBeAppException(400, ErrorCodes.BadRequest, "chờ hoàn tiền");
        builder.RefundRepository.Verify(x => x.GetByBookingIdAsync(booking.Id), Times.Never);
        builder.RefundRepository.Verify(x => x.UpdateAsync(It.IsAny<Refund>()), Times.Never);
        builder.PaymentRepository.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Never);
        builder.InvoiceRepository.Verify(x => x.UpdateAsync(It.IsAny<Invoice>()), Times.Never);
        builder.BookingRepository.Verify(x => x.UpdateAsync(It.IsAny<Booking>()), Times.Never);
        builder.LoyaltyTransactionRepository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfirmRefundAsync_WhenEarnTransactionExists_DeductsProportionalPointsAndDowngradesTier()
    {
        var builder = new BookingServiceTestBuilder();
        var customer = TestDataFactory.CreateUser();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(
            customerId: customer.Id, branchId: branch.Id,
            status: BookingStatus.CANCELLED_PENDING_REFUND);
        booking.Customer = customer;
        booking.Branch = branch;
        var payment = TestDataFactory.CreatePayment(amount: 200_000m, status: PaymentTxStatus.SUCCESS);
        var invoice = TestDataFactory.CreateInvoice(
            booking.Id, finalTotal: 200_000m, paymentStatus: InvoicePaymentStatus.PAID,
            payments: [payment]);
        invoice.Booking = booking;
        payment.Invoice = invoice;
        booking.Invoice = invoice;
        var refund = new Refund
        {
            Id = Guid.NewGuid(), PaymentId = payment.Id, Payment = payment,
            Amount = 100_000m, RefundPercent = 50m, Status = RefundStatus.PENDING
        };
        var goldId = Guid.NewGuid();
        var silverId = Guid.NewGuid();
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.RefundRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(refund);
        builder.LoyaltyTransactionRepository.Setup(x => x.GetByBookingIdAsync(booking.Id))
            .ReturnsAsync(new LoyaltyTransaction
            {
                BookingId = booking.Id, UserId = customer.Id, Points = 200,
                TotalPointsAfter = 5_100, Type = LoyaltyTransactionType.EARN
            });
        builder.LoyaltyTransactionRepository.Setup(x => x.GetDeductByBookingIdAsync(booking.Id))
            .ReturnsAsync((LoyaltyTransaction?)null);
        builder.LoyaltyRepository.Setup(x => x.GetByUserIdAsync(customer.Id))
            .ReturnsAsync(new CustomerLoyalty { UserId = customer.Id, TierId = goldId, TotalPoints = 5_100 });
        builder.LoyaltyRepository.Setup(x => x.AddPointsAtomicAsync(customer.Id, -100))
            .ReturnsAsync(5_000);
        builder.LoyaltyTierRepository.Setup(x => x.GetAllLoyaltyTiersAsync())
            .ReturnsAsync([
                new LoyaltyTier { Id = silverId, Name = "Silver", MinPoints = 1_000 },
                new LoyaltyTier { Id = goldId, Name = "Gold", MinPoints = 5_100 }
            ]);

        await builder.Build().ConfirmRefundAsync(
            booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        builder.LoyaltyRepository.Verify(x => x.AddPointsAtomicAsync(customer.Id, -100), Times.Once);
        builder.LoyaltyRepository.Verify(x => x.UpdateTierAsync(customer.Id, silverId), Times.Once);
        builder.LoyaltyTransactionRepository.Verify(x => x.AddAsync(It.Is<LoyaltyTransaction>(transaction =>
            transaction.BookingId == booking.Id && transaction.Points == -100 &&
            transaction.TotalPointsAfter == 5_000 && transaction.Type == LoyaltyTransactionType.DEDUCT)), Times.Once);
    }

    [Fact]
    public async Task ConfirmRefundAsync_WhenDeductionAlreadyExists_DoesNotDeductAgain()
    {
        var builder = new BookingServiceTestBuilder();
        var customer = TestDataFactory.CreateUser();
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var booking = TestDataFactory.CreateBooking(
            customerId: customer.Id, branchId: branch.Id,
            status: BookingStatus.CANCELLED_PENDING_REFUND);
        booking.Customer = customer;
        booking.Branch = branch;
        var payment = TestDataFactory.CreatePayment(amount: 200_000m, status: PaymentTxStatus.SUCCESS);
        var invoice = TestDataFactory.CreateInvoice(
            booking.Id, finalTotal: 200_000m, paymentStatus: InvoicePaymentStatus.PAID,
            payments: [payment]);
        invoice.Booking = booking;
        payment.Invoice = invoice;
        booking.Invoice = invoice;
        var refund = new Refund
        {
            Id = Guid.NewGuid(), PaymentId = payment.Id, Payment = payment,
            Amount = 100_000m, RefundPercent = 50m, Status = RefundStatus.PENDING
        };

        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.RefundRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(refund);
        builder.LoyaltyTransactionRepository.Setup(x => x.GetByBookingIdAsync(booking.Id))
            .ReturnsAsync(new LoyaltyTransaction { BookingId = booking.Id, Points = 200, Type = LoyaltyTransactionType.EARN });
        builder.LoyaltyTransactionRepository.Setup(x => x.GetDeductByBookingIdAsync(booking.Id))
            .ReturnsAsync(new LoyaltyTransaction { BookingId = booking.Id, Points = -100, Type = LoyaltyTransactionType.DEDUCT });

        await builder.Build().ConfirmRefundAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        builder.LoyaltyRepository.Verify(x => x.AddPointsAtomicAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        builder.LoyaltyTransactionRepository.Verify(x => x.AddAsync(It.IsAny<LoyaltyTransaction>()), Times.Never);
    }

    [Fact]
    public async Task CheckoutAsync_WhenInvoiceIsPartiallyPaid_CollectsOnlyOutstandingServiceFee()
    {
        var builder = new BookingServiceTestBuilder();
        var invoice = TestDataFactory.CreateInvoice(
            finalTotal: 250_000m,
            paymentStatus: InvoicePaymentStatus.PARTIALLY_PAID);
        invoice.CourtFee = 200_000m;
        invoice.ServiceFee = 50_000m;
        var booking = CreateCheckoutBooking(BookingStatus.PENDING_PAYMENT, invoice);
        booking.BookingCourts.Single().EndTime = new TimeOnly(23, 59);
        builder.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(booking.Id)).ReturnsAsync(booking);
        builder.BookingRepository.Setup(x => x.UpdateWithStatusCheckAsync(
                booking.Id, BookingStatus.COMPLETED, BookingStatus.PENDING_PAYMENT))
            .ReturnsAsync(1);
        builder.InvoiceRepository.Setup(x => x.GetByBookingIdAsync(booking.Id)).ReturnsAsync(invoice);

        await builder.Build().CollectPaymentAsync(booking.Id, Guid.NewGuid(), UserRole.OWNER.ToString());

        Assert.Equal(BookingStatus.COMPLETED, booking.Status);
        Assert.Equal(InvoicePaymentStatus.PAID, invoice.PaymentStatus);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.Is<Payment>(payment =>
            payment.InvoiceId == invoice.Id && payment.Amount == 50_000m)), Times.Once);
        builder.PaymentRepository.Verify(x => x.CreateAsync(It.Is<Payment>(payment =>
            payment.Amount == 250_000m)), Times.Never);
        builder.BookingRepository.Verify(x => x.UpdateWithStatusCheckAsync(
            booking.Id, BookingStatus.COMPLETED, BookingStatus.PENDING_PAYMENT), Times.Once);
    }

    private static Booking CreateCheckoutBooking(BookingStatus status, Invoice invoice)
    {
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(branchId: branch.Id, status: status);
        booking.Branch = branch;
        booking.Invoice = invoice;
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id,
            CourtId = court.Id,
            Date = booking.BookingDate,
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(19, 30),
            Court = court,
            IsActive = true
        });
        invoice.BookingId = booking.Id;
        return booking;
    }

    private static Booking CreateCheckInBooking(BookingStatus status, DateTime startVietnam)
    {
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Test branch" };
        var court = TestDataFactory.CreateCourt(branch.Id);
        var booking = TestDataFactory.CreateBooking(
            branchId: branch.Id,
            status: status,
            bookingDate: DateOnly.FromDateTime(startVietnam));
        booking.Branch = branch;
        booking.BookingCourts.Add(new BookingCourt
        {
            BookingId = booking.Id,
            CourtId = court.Id,
            Date = booking.BookingDate,
            StartTime = TimeOnly.FromDateTime(startVietnam),
            EndTime = TimeOnly.FromDateTime(startVietnam.AddMinutes(90)),
            Court = court,
            IsActive = true
        });
        return booking;
    }

    private static CreateOnlineBookingDto CreateOnlineBookingDto(params Guid[] courtIds)
    {
        return new CreateOnlineBookingDto
        {
            BookingDate = TestConstants.StandardDateTime,
            GuestName = "Guest",
            GuestPhone = "0900000000",
            GuestEmail = "guest@example.com",
            Courts = courtIds.Select(courtId => new CourtSlotDto
            {
                CourtId = courtId,
                StartTime = TestConstants.EveningStartTime,
                EndTime = TestConstants.EveningEndTime
            }).ToList()
        };
    }

    private static CreateWalkInBookingDto CreateWalkInBookingDto(Guid courtId) => new()
    {
        BookingDate = TestConstants.StandardDateTime,
        GuestName = "Walk-in guest",
        GuestPhone = "0900000000",
        Courts =
        [
            new CourtSlotDto
            {
                CourtId = courtId,
                StartTime = TestConstants.EveningStartTime,
                EndTime = TestConstants.EveningEndTime
            }
        ]
    };
}
