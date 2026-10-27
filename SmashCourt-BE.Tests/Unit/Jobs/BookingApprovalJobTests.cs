using System.Reflection;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Jobs;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Jobs;

/// <summary>
/// Job hết hạn duyệt (gói 6A-5 Task 4, spec 7.11): tối đa 100 đơn PENDING_APPROVAL quá hạn duyệt mỗi lần, hạn sớm nhất trước; mỗi đơn qua
/// dịch vụ hủy chung (APPROVAL_EXPIRED). "Không bao giờ tự duyệt" bảo đảm bằng cấu trúc: job không nhận TADSS client hay dịch vụ duyệt.
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingApprovalJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IBookingCancellationService> _cancellation = new();
    private readonly ListLogger<BookingApprovalJob> _logger = new();

    private BookingApprovalJob CreateJob() => new(_bookings.Object, _cancellation.Object, new FakeTimeProvider(Now), _logger);

    private void GivenExpired(params Guid[] ids) =>
        _bookings.Setup(x => x.GetExpiredApprovalIdsAsync(Now.UtcDateTime, 100)).ReturnsAsync(ids.ToList());

    [Fact]
    public async Task ExpireApprovals_AsksForAtMost100OrdersAtTheCurrentClock()
    {
        GivenExpired();

        await CreateJob().ExpireApprovalsAsync();

        _bookings.Verify(x => x.GetExpiredApprovalIdsAsync(Now.UtcDateTime, 100), Times.Once);
    }

    [Fact]
    public async Task ExpireApprovals_ExpiresEachOrderInTheGivenOrder_NeverThePaymentPath()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        GivenExpired(ids);
        var called = new List<Guid>();
        _cancellation.Setup(x => x.ExpireApprovalAsync(It.IsAny<Guid>())).Callback<Guid>(called.Add).ReturnsAsync(true);

        await CreateJob().ExpireApprovalsAsync();

        Assert.Equal(ids, called);
        _cancellation.Verify(x => x.ExpireUnpaidAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ExpireApprovals_NoExpiredOrder_CallsNothing()
    {
        GivenExpired();

        await CreateJob().ExpireApprovalsAsync();

        _cancellation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExpireApprovals_OneOrderThrows_LogsErrorWithIdAndContinues()
    {
        var broken = Guid.NewGuid();
        var next = Guid.NewGuid();
        GivenExpired(broken, next);
        _cancellation.Setup(x => x.ExpireApprovalAsync(broken)).ThrowsAsync(new InvalidOperationException("DB hỏng"));
        _cancellation.Setup(x => x.ExpireApprovalAsync(next)).ReturnsAsync(true);

        await CreateJob().ExpireApprovalsAsync();

        _cancellation.Verify(x => x.ExpireApprovalAsync(next), Times.Once);
        var error = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(broken.ToString(), error.Message);
    }

    [Fact]
    public async Task ExpireApprovals_LogsCandidatesCancelledSkippedAndFailed()
    {
        var cancelled = Guid.NewGuid();
        var skipped = Guid.NewGuid();
        var failed = Guid.NewGuid();
        GivenExpired(cancelled, skipped, failed);
        _cancellation.Setup(x => x.ExpireApprovalAsync(cancelled)).ReturnsAsync(true);
        _cancellation.Setup(x => x.ExpireApprovalAsync(skipped)).ReturnsAsync(false);
        _cancellation.Setup(x => x.ExpireApprovalAsync(failed)).ThrowsAsync(new TimeoutException());

        await CreateJob().ExpireApprovalsAsync();

        var summary = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("ứng viên"));
        Assert.Contains("3 ứng viên", summary.Message);
        Assert.Contains("đã hủy 1", summary.Message);
        Assert.Contains("bỏ qua 1", summary.Message);
        Assert.Contains("lỗi 1", summary.Message);
    }

    [Fact]
    public async Task ExpireApprovals_CandidateQueryThrows_DoesNotThrowToHangfire()
    {
        _bookings.Setup(x => x.GetExpiredApprovalIdsAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ThrowsAsync(new TimeoutException());

        var ex = await Record.ExceptionAsync(() => CreateJob().ExpireApprovalsAsync());

        Assert.Null(ex);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public void Job_CannotApprove_ItDependsOnNoTadssClientOrApprovalService()
    {
        var parameterTypes = typeof(BookingApprovalJob).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(typeof(ITadssClient), parameterTypes);
        Assert.DoesNotContain(typeof(IBookingApprovalService), parameterTypes);
        Assert.Equal(
            [typeof(IBookingRepository), typeof(IBookingCancellationService), typeof(TimeProvider)],
            parameterTypes.Take(3));
    }

    [Fact]
    public void ExpireApprovals_TwoInstancesNeverRunAtTheSameTime()
    {
        var method = typeof(BookingApprovalJob).GetMethod(nameof(BookingApprovalJob.ExpireApprovalsAsync));

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<DisableConcurrentExecutionAttribute>());
    }
}
