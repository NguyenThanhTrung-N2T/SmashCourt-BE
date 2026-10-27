using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Data;
using SmashCourt_BE.Jobs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>
/// Dựng <see cref="BookingJob"/> với repository giả cho unit test của Job-01, Job-02, Job-04. Các job này không còn truy vấn DbContext trực tiếp;
/// context chỉ để dựng job (Job-03 vẫn dùng), không mở kết nối.
/// </summary>
internal sealed class BookingJobTestBuilder
{
    /// <summary>"Bây giờ" mặc định: 20/11/2026 17:00 giờ VN.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Ngày VN của <see cref="DefaultNow"/>.</summary>
    public static readonly DateOnly Today = new(2026, 11, 20);

    public Mock<IBookingRepository> BookingRepository { get; } = new();
    public Mock<IInvoiceRepository> InvoiceRepository { get; } = new();
    public Mock<ICourtRepository> CourtRepository { get; } = new();
    public Mock<IBookingCancellationService> CancellationService { get; } = new();
    public Mock<ISlotInterestNotificationService> SlotInterestNotifier { get; } = new();
    public Mock<IBroadcastService> Broadcast { get; } = new();
    public FakeTimeProvider Time { get; } = new(DefaultNow);
    public ListLogger<BookingJob> Logger { get; } = new();

    public BookingJobTestBuilder()
    {
        // Mặc định: không sân nào bị đơn khác giữ
        BookingRepository.Setup(x => x.GetCourtsHeldByOtherBookingsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>()))
            .ReturnsAsync([]);
    }

    public BookingJob Build()
    {
        var db = new SmashCourtContext(new DbContextOptionsBuilder<SmashCourtContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        return new BookingJob(
            db,
            Mock.Of<ISlotInterestRepository>(),
            Broadcast.Object,
            SlotInterestNotifier.Object,
            BookingRepository.Object,
            InvoiceRepository.Object,
            CourtRepository.Object,
            CancellationService.Object,
            Time,
            Logger);
    }

    /// <summary>Đơn một hoặc nhiều sân cùng khung giờ (giờ VN) ngày <see cref="Today"/>.</summary>
    public static Booking Booking(BookingStatus status, TimeOnly start, TimeOnly end, int courtCount = 1, DateOnly? date = null,
        InvoicePaymentStatus? invoiceStatus = InvoicePaymentStatus.UNPAID)
    {
        var playDate = date ?? Today;
        var booking = TestDataFactory.CreateBooking(status: status, bookingDate: playDate);
        booking.BookingCode = "BK-JOB";
        booking.Customer = new User { Id = booking.CustomerId!.Value, FullName = "Nguyễn Văn A", Email = "khach@example.com" };
        booking.Branch = new Branch { Id = booking.BranchId, Name = "Chi nhánh 1" };
        for (var i = 0; i < courtCount; i++)
        {
            var court = TestDataFactory.CreateCourt(booking.BranchId);
            booking.BookingCourts.Add(new BookingCourt
            {
                Id = Guid.NewGuid(), BookingId = booking.Id, CourtId = court.Id, Court = court, Date = playDate,
                StartTime = start, EndTime = end, IsActive = true
            });
        }
        if (invoiceStatus is { } paymentStatus)
            booking.Invoice = TestDataFactory.CreateInvoice(booking.Id, 200_000m, paymentStatus);
        if (status == BookingStatus.IN_PROGRESS)
            booking.CheckedInAt = DefaultNow.UtcDateTime.AddHours(-1);
        return booking;
    }
}
