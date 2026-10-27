using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Jobs;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Jobs;

/// <summary>
/// Job-01 viết lại (gói 6A-5 Task 3, spec 7.11): lấy tối đa 100 đơn PENDING quá hạn (hạn sớm nhất trước), gọi dịch vụ hủy chung từng đơn
/// (mỗi đơn một transaction, có điều kiện); một đơn lỗi không chặn đơn sau; log tổng đủ bốn số; job không ném exception ra Hangfire.
/// Phần ghi DB thật (C4, C5, slot lock) kiểm ở test repository và Task 11.
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingJobExpiryTests
{
    private static readonly DateTimeOffset Now = BookingJobTestBuilder.DefaultNow;

    private readonly BookingJobTestBuilder _builder = new();
    private readonly Mock<IBookingRepository> _bookings;
    private readonly Mock<IBookingCancellationService> _cancellation;
    private readonly ListLogger<BookingJob> _logger;

    public BookingJobExpiryTests()
    {
        _bookings = _builder.BookingRepository;
        _cancellation = _builder.CancellationService;
        _logger = _builder.Logger;
    }

    private BookingJob CreateJob() => _builder.Build();

    private void GivenExpired(params Guid[] ids) =>
        _bookings.Setup(x => x.GetExpiredUnpaidIdsAsync(Now.UtcDateTime, 100)).ReturnsAsync(ids.ToList());

    [Fact]
    public async Task CancelExpired_AsksForAtMost100OrdersAtTheCurrentClock()
    {
        GivenExpired();

        await CreateJob().CancelExpiredPendingBookingsAsync();

        _bookings.Verify(x => x.GetExpiredUnpaidIdsAsync(Now.UtcDateTime, 100), Times.Once);
    }

    [Fact]
    public async Task CancelExpired_ExpiresEachOrderInTheGivenOrder()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        GivenExpired(ids);
        var called = new List<Guid>();
        _cancellation.Setup(x => x.ExpireUnpaidAsync(It.IsAny<Guid>())).Callback<Guid>(called.Add).ReturnsAsync(true);

        await CreateJob().CancelExpiredPendingBookingsAsync();

        Assert.Equal(ids, called);
        _cancellation.Verify(x => x.ExpireApprovalAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CancelExpired_NoExpiredOrder_CallsNothing()
    {
        GivenExpired();

        await CreateJob().CancelExpiredPendingBookingsAsync();

        _cancellation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelExpired_OneOrderThrows_LogsErrorWithIdAndContinues()
    {
        var broken = Guid.NewGuid();
        var next = Guid.NewGuid();
        GivenExpired(broken, next);
        _cancellation.Setup(x => x.ExpireUnpaidAsync(broken)).ThrowsAsync(new InvalidOperationException("DB hỏng"));
        _cancellation.Setup(x => x.ExpireUnpaidAsync(next)).ReturnsAsync(true);

        await CreateJob().CancelExpiredPendingBookingsAsync();

        _cancellation.Verify(x => x.ExpireUnpaidAsync(next), Times.Once);
        var error = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(broken.ToString(), error.Message);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task CancelExpired_LogsCandidatesCancelledSkippedAndFailed()
    {
        var cancelled1 = Guid.NewGuid();
        var cancelled2 = Guid.NewGuid();
        var skipped = Guid.NewGuid();
        var failed = Guid.NewGuid();
        GivenExpired(cancelled1, skipped, failed, cancelled2);
        _cancellation.Setup(x => x.ExpireUnpaidAsync(cancelled1)).ReturnsAsync(true);
        _cancellation.Setup(x => x.ExpireUnpaidAsync(cancelled2)).ReturnsAsync(true);
        _cancellation.Setup(x => x.ExpireUnpaidAsync(skipped)).ReturnsAsync(false);
        _cancellation.Setup(x => x.ExpireUnpaidAsync(failed)).ThrowsAsync(new TimeoutException());

        await CreateJob().CancelExpiredPendingBookingsAsync();

        var summary = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("ứng viên"));
        Assert.Contains("4 ứng viên", summary.Message);
        Assert.Contains("đã hủy 2", summary.Message);
        Assert.Contains("bỏ qua 1", summary.Message);
        Assert.Contains("lỗi 1", summary.Message);
    }

    [Fact]
    public async Task CancelExpired_CandidateQueryThrows_DoesNotThrowToHangfire()
    {
        _bookings.Setup(x => x.GetExpiredUnpaidIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ThrowsAsync(new TimeoutException());

        var ex = await Record.ExceptionAsync(() => CreateJob().CancelExpiredPendingBookingsAsync());

        Assert.Null(ex);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
        _cancellation.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(BookingJob.CancelExpiredPendingBookingsAsync))]
    [InlineData(nameof(BookingJob.ProcessExpiredActiveBookingsAsync))]
    [InlineData(nameof(BookingJob.CleanupExpiredSlotLocksAsync))]
    [InlineData(nameof(BookingJob.DetectNoShowBookingsAsync))]
    [InlineData(nameof(BookingJob.CleanupExpiredSlotInterestsAsync))]
    public void EveryBookingJob_TwoInstancesNeverRunAtTheSameTime(string methodName)
    {
        var method = typeof(BookingJob).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.NotNull(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<Hangfire.DisableConcurrentExecutionAttribute>(method));
    }
}
