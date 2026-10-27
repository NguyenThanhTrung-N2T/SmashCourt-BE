using System.Reflection;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Jobs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Jobs;

/// <summary>
/// Job báo kết quả về TADSS (gói 6A-5 Task 9, spec 7.11, P6-19, P6-26): mỗi chu kỳ tối đa 20 đơn, gửi tuần tự; 200 hoặc 409
/// OUTCOME_ALREADY_RECORDED ⇒ đánh dấu; lỗi riêng một đơn (404 DECISION_NOT_FOUND, 400 VALIDATION_ERROR) ⇒ làm tiếp; mọi lỗi hệ thống ⇒ dừng
/// chu kỳ (C3); SYSTEM_CLEANUP ⇒ đánh dấu không gửi; nhãn lệch checked_in_at ⇒ giữ lại, log Error. Chạy cả khi tắt cờ đánh giá.
/// </summary>
[Trait("Category", TestCategories.Tadss)]
public class TadssOutcomeJobTests
{
    // 20/11/2026 17:00 giờ VN
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateTime CancelledAt = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<ITadssClient> _tadss = new();
    private readonly ListLogger<TadssOutcomeJob> _logger = new();
    private readonly List<Guid> _sent = [];

    public TadssOutcomeJobTests()
    {
        _bookings.Setup(x => x.MarkOutcomeReportedAsync(It.IsAny<Guid>(), It.IsAny<DateTime>())).ReturnsAsync(true);
        _tadss.Setup(x => x.ReportOutcomeAsync(It.IsAny<Guid>(), It.IsAny<TadssOutcomeRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, TadssOutcomeRequestDto, CancellationToken>((id, _, _) => _sent.Add(id))
            .ReturnsAsync((Guid id, TadssOutcomeRequestDto request, CancellationToken _) => Ok(id, request));
    }

    private TadssOutcomeJob CreateJob() => new(_bookings.Object, _tadss.Object, new FakeTimeProvider(Now), _logger);

    private static TadssCallResult<TadssOutcomeResponseDto> Ok(Guid decisionId, TadssOutcomeRequestDto request) =>
        TadssCallResult<TadssOutcomeResponseDto>.Success(new TadssOutcomeResponseDto
        {
            DecisionId = decisionId, Outcome = request.Outcome, OccurredAt = request.OccurredAt, ReportedAt = Now
        }, 200, "corr");

    private static TadssCallResult<TadssOutcomeResponseDto> Fail(
        TadssFailureKind kind, int? status = null, string? code = null, TimeSpan? retryAfter = null) =>
        TadssCallResult<TadssOutcomeResponseDto>.Failure(kind, "corr", status, code, retryAfter);

    /// <summary>Đơn hủy do khách (gửi CANCELLED_BY_CUSTOMER) có quyết định TADSS.</summary>
    private static Booking Cancelled(BookingCancelReason reason = BookingCancelReason.CUSTOMER_CANCELLED)
    {
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.CANCELLED, bookingDate: Today);
        booking.TadssDecisionId = Guid.NewGuid();
        booking.CancelReason = reason;
        booking.CancelledAt = CancelledAt;
        booking.UpdatedAt = CancelledAt;
        return booking;
    }

    /// <summary>Đơn đã chơi 15:00–16:00 hôm nay; checkedIn quyết định nhãn có khớp dữ liệu không.</summary>
    private static Booking Played(BookingStatus status, bool checkedIn, TimeOnly? end = null)
    {
        var booking = TestDataFactory.CreateBooking(status: status, bookingDate: Today);
        booking.TadssDecisionId = Guid.NewGuid();
        booking.CheckedInAt = checkedIn ? CancelledAt : null;
        booking.UpdatedAt = CancelledAt;
        booking.BookingCourts.Add(new BookingCourt { CourtId = Guid.NewGuid(), Date = Today, StartTime = new TimeOnly(15, 0), EndTime = end ?? new TimeOnly(16, 0) });
        return booking;
    }

    private void GivenCandidates(params Booking[] bookings) =>
        _bookings.Setup(x => x.GetOutcomeCandidatesAsync(Today, new TimeOnly(17, 0), 20)).ReturnsAsync(bookings.ToList());

    private void GivenReply(Booking booking, TadssCallResult<TadssOutcomeResponseDto> result) =>
        _tadss.Setup(x => x.ReportOutcomeAsync(DecisionOf(booking), It.IsAny<TadssOutcomeRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, TadssOutcomeRequestDto, CancellationToken>((id, _, _) => _sent.Add(id))
            .ReturnsAsync(result);

    private void VerifyMarked(Booking booking, Times times) =>
        _bookings.Verify(x => x.MarkOutcomeReportedAsync(booking.Id, Now.UtcDateTime), times);

    private ListLogger<TadssOutcomeJob>.Entry Summary() => Assert.Single(_logger.Entries, e => e.Message.Contains("[Outcome] Chu kỳ"));

    private static Guid DecisionOf(Booking booking) =>
        booking.TadssDecisionId ?? throw new InvalidOperationException("Đơn mẫu phải có quyết định TADSS");

    // ── Chu kỳ bình thường ────────────────────────────────────────────────

    [Fact]
    public async Task Report_AsksFor20CandidatesAtTheVietnamClock_AndSendsEachInOrder()
    {
        var candidates = Enumerable.Range(0, 20).Select(_ => Cancelled()).ToArray();
        GivenCandidates(candidates);

        await CreateJob().ReportOutcomesAsync();

        _bookings.Verify(x => x.GetOutcomeCandidatesAsync(Today, new TimeOnly(17, 0), 20), Times.Once);
        Assert.Equal(candidates.Select(b => DecisionOf(b)), _sent);
        foreach (var booking in candidates)
            VerifyMarked(booking, Times.Once());
    }

    [Fact]
    public async Task Report_SendsTheMappedOutcomeAndOccurredAt()
    {
        var booking = Played(BookingStatus.PENDING_PAYMENT, checkedIn: true);
        GivenCandidates(booking);
        TadssOutcomeRequestDto? request = null;
        _tadss.Setup(x => x.ReportOutcomeAsync(DecisionOf(booking), It.IsAny<TadssOutcomeRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, TadssOutcomeRequestDto, CancellationToken>((_, r, _) => request = r)
            .ReturnsAsync((Guid id, TadssOutcomeRequestDto r, CancellationToken _) => Ok(id, r));

        await CreateJob().ReportOutcomesAsync();

        Assert.NotNull(request);
        Assert.Equal(TadssOutcome.COMPLETED, request.Outcome);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 16, 0, 0, TimeSpan.FromHours(7)), request.OccurredAt);
    }

    // ── Lỗi hệ thống: dừng chu kỳ (C3) ────────────────────────────────────

    public static TheoryData<string> SystemFailures => new()
    {
        "429", "503", "timeout", "network", "invalid-body", "401", "404-html", "500"
    };

    private static TadssCallResult<TadssOutcomeResponseDto> SystemFailure(string name) => name switch
    {
        "429" => Fail(TadssFailureKind.Http, 429, "RATE_LIMITED", TimeSpan.FromSeconds(3)),
        "503" => Fail(TadssFailureKind.Http, 503, "PERSISTENCE_UNAVAILABLE"),
        "timeout" => Fail(TadssFailureKind.Timeout),
        "network" => Fail(TadssFailureKind.Network),
        "invalid-body" => Fail(TadssFailureKind.InvalidResponse, 200),  // gồm 200 sai decisionId (client kiểm, B4)
        "401" => Fail(TadssFailureKind.Http, 401, "INVALID_API_KEY"),
        "404-html" => Fail(TadssFailureKind.Http, 404),
        _ => Fail(TadssFailureKind.Http, 500, "INTERNAL_ERROR")
    };

    [Theory]
    [MemberData(nameof(SystemFailures))]
    public async Task Report_SystemFailureOnTheThirdOrder_MarksTheFirstTwoAndStopsTheCycle(string failure)
    {
        var candidates = Enumerable.Range(0, 20).Select(_ => Cancelled()).ToArray();
        GivenCandidates(candidates);
        GivenReply(candidates[2], SystemFailure(failure));

        await CreateJob().ReportOutcomesAsync();

        Assert.Equal(candidates.Take(3).Select(b => DecisionOf(b)), _sent); // đơn 4…20 không được gửi
        VerifyMarked(candidates[0], Times.Once());
        VerifyMarked(candidates[1], Times.Once());
        VerifyMarked(candidates[2], Times.Never());
        Assert.Contains("dừng", Summary().Message);
    }

    [Fact]
    public async Task Report_RateLimited_LogsRetryAfter()
    {
        var booking = Cancelled();
        GivenCandidates(booking);
        GivenReply(booking, SystemFailure("429"));

        await CreateJob().ReportOutcomesAsync();

        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("429") && e.Message.Contains("00:00:03"));
    }

    // ── Lỗi riêng một đơn: làm tiếp ───────────────────────────────────────

    [Theory]
    [InlineData(404, "DECISION_NOT_FOUND")]
    [InlineData(400, "VALIDATION_ERROR")]
    public async Task Report_TadssRejectsOneOrder_LeavesItAndContinues(int status, string code)
    {
        var rejected = Cancelled();
        var next = Cancelled();
        GivenCandidates(rejected, next);
        GivenReply(rejected, Fail(TadssFailureKind.Http, status, code));

        await CreateJob().ReportOutcomesAsync();

        VerifyMarked(rejected, Times.Never());
        VerifyMarked(next, Times.Once());
        var warning = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(code));
        Assert.Contains(rejected.Id.ToString(), warning.Message);
        Assert.Contains(DecisionOf(rejected).ToString(), warning.Message);
        Assert.Contains(status.ToString(), warning.Message);
    }

    [Fact]
    public async Task Report_OutcomeAlreadyRecorded_MarksWithAWarning()
    {
        var booking = Cancelled();
        GivenCandidates(booking);
        GivenReply(booking, Fail(TadssFailureKind.Http, 409, "OUTCOME_ALREADY_RECORDED"));

        await CreateJob().ReportOutcomesAsync();

        VerifyMarked(booking, Times.Once());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("OUTCOME_ALREADY_RECORDED")
            && e.Message.Contains(nameof(TadssOutcome.CANCELLED_BY_CUSTOMER)));
    }

    // ── Không gửi ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Report_SystemCleanup_MarksWithoutCallingTadss()
    {
        var booking = Cancelled(BookingCancelReason.SYSTEM_CLEANUP);
        GivenCandidates(booking);

        await CreateJob().ReportOutcomesAsync();

        Assert.Empty(_sent);
        VerifyMarked(booking, Times.Once());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(booking.Id.ToString()));
    }

    [Theory]
    [InlineData(BookingStatus.NO_SHOW, true)]
    [InlineData(BookingStatus.COMPLETED, false)]
    public async Task Report_LabelContradictsCheckIn_KeepsItAndLogsError(BookingStatus status, bool checkedIn)
    {
        var booking = Played(status, checkedIn);
        GivenCandidates(booking);

        await CreateJob().ReportOutcomesAsync();

        Assert.Empty(_sent);
        VerifyMarked(booking, Times.Never());
        var error = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(booking.Id.ToString(), error.Message);
        Assert.Contains(DecisionOf(booking).ToString(), error.Message);
        Assert.Contains("checked_in_at", error.Message);
    }

    [Fact]
    public async Task Report_PlayNotEndedYet_IsLeftForALaterCycle()
    {
        var booking = Played(BookingStatus.COMPLETED, checkedIn: true, end: new TimeOnly(18, 0)); // check-out sớm
        GivenCandidates(booking);

        await CreateJob().ReportOutcomesAsync();

        Assert.Empty(_sent);
        VerifyMarked(booking, Times.Never());
    }

    // ── Biên ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Report_MarkReturnsFalseBecauseAnotherRunMarked_IsNotAnError()
    {
        var booking = Cancelled();
        GivenCandidates(booking);
        _bookings.Setup(x => x.MarkOutcomeReportedAsync(booking.Id, It.IsAny<DateTime>())).ReturnsAsync(false);

        var ex = await Record.ExceptionAsync(() => CreateJob().ReportOutcomesAsync());

        Assert.Null(ex);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Report_RepositoryThrows_IsCaughtAndLogged()
    {
        _bookings.Setup(x => x.GetOutcomeCandidatesAsync(It.IsAny<DateOnly>(), It.IsAny<TimeOnly>(), It.IsAny<int>()))
            .ThrowsAsync(new TimeoutException());

        var ex = await Record.ExceptionAsync(() => CreateJob().ReportOutcomesAsync());

        Assert.Null(ex);
        Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Report_NoCandidate_CallsNothing()
    {
        GivenCandidates();

        await CreateJob().ReportOutcomesAsync();

        _tadss.VerifyNoOtherCalls();
    }

    // ── Log tổng ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Report_SummaryCountsEveryKindOfOrder()
    {
        var sent = Cancelled();
        var cleanup = Cancelled(BookingCancelReason.SYSTEM_CLEANUP);
        var notDue = Played(BookingStatus.COMPLETED, checkedIn: true, end: new TimeOnly(18, 0));
        var invalid = Played(BookingStatus.NO_SHOW, checkedIn: true);
        var rejected = Cancelled();
        rejected.UpdatedAt = CancelledAt.AddHours(-30); // đơn chưa báo cũ nhất trong lô
        var failed = Cancelled();
        GivenCandidates(sent, cleanup, notDue, invalid, rejected, failed);
        GivenReply(rejected, Fail(TadssFailureKind.Http, 404, "DECISION_NOT_FOUND"));
        GivenReply(failed, Fail(TadssFailureKind.Timeout));

        await CreateJob().ReportOutcomesAsync();

        var summary = Summary().Message;
        Assert.Contains("6 ứng viên", summary);
        Assert.Contains("đã báo 1", summary);
        Assert.Contains("bỏ qua 1", summary);
        Assert.Contains("chưa tới giờ 1", summary);
        Assert.Contains("lệch dữ liệu 1", summary);
        Assert.Contains("bị từ chối 1", summary);
        Assert.Contains("dừng: Timeout", summary);
        Assert.Contains("1d07h00m", summary); // 10:00 UTC hôm nay − 03:00 UTC hôm qua
    }

    // ── Cấu trúc ──────────────────────────────────────────────────────────

    [Fact]
    public void Job_DoesNotReadTheEvaluationFlag_SoItRunsEvenWhenTheFlagIsOff()
    {
        var parameterTypes = typeof(TadssOutcomeJob).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(typeof(IOptions<TadssSettings>), parameterTypes);
        Assert.DoesNotContain(typeof(IOptionsMonitor<TadssSettings>), parameterTypes);
        Assert.DoesNotContain(typeof(TadssSettings), parameterTypes);
    }

    [Fact]
    public void Report_TwoInstancesNeverRunAtTheSameTime()
    {
        var method = typeof(TadssOutcomeJob).GetMethod(nameof(TadssOutcomeJob.ReportOutcomesAsync));

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<DisableConcurrentExecutionAttribute>());
    }

    [Fact]
    public async Task Report_CandidateWithoutDecision_IsNotSentAndIsLogged()
    {
        var booking = Cancelled();
        booking.TadssDecisionId = null;
        var next = Cancelled();
        GivenCandidates(booking, next);

        await CreateJob().ReportOutcomesAsync();

        Assert.Equal([DecisionOf(next)], _sent);
        VerifyMarked(booking, Times.Never());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(booking.Id.ToString()));
    }
}
