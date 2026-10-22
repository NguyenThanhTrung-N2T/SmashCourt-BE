using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Services;

/// <summary>BookingDepositService.EvaluateAsync: nối snapshot, lịch sử, mapper và client TADSS (thành công, lỗi, lỗi lập trình).</summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Tadss)]
[Trait("Catalog", "BE-UT-02")]
public class BookingDepositServiceTests
{
    private static readonly Guid BookingId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime CreatedAt = new(2026, 11, 18, 2, 15, 0, DateTimeKind.Utc);

    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<ICustomerHistoryRepository> _history = new();
    private readonly Mock<ITadssClient> _client = new();
    private readonly TadssSettings _settings = new() { BranchTimeZone = "Asia/Ho_Chi_Minh" };
    private readonly BookingPaymentSettings _paymentSettings = new() { MinOnlinePaymentAmount = 5000m };
    private readonly ListLogger<BookingDepositService> _logger = new();
    private readonly List<string> _calls = [];

    private static BookingDecisionSnapshot Snapshot() => new(
        BookingId, Guid.NewGuid(), CustomerId, CreatedAt, 228_000m, "WINTER",
        new DateTime(2025, 3, 14, 1, 0, 0, DateTimeKind.Utc), 3,
        [new BookingDecisionCourt(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 11, 20), new TimeOnly(18, 0), new TimeOnly(20, 0), 240_000m)]);

    private static TadssDecisionResultDto Approved() => new()
    {
        DecisionId = Guid.NewGuid(), ReservationId = BookingId, Status = TadssDecisionStatus.APPROVED,
        Deposit = new TadssDepositDto { Rate = 0.5m, Amount = 114_000m, Currency = "VND" },
        StrategyName = "DefaultDecisionStrategy", StrategyVersion = "2.1.0", ExecutionType = "AUTOMATED", FallbackUsed = false,
        ReasonCode = "TIER_DEPOSIT", Reasons = [], Recommendations = [], EvaluatedAt = CreatedAt
    };

    private BookingDepositService CreateService(BookingDecisionSnapshot? snapshot, IReadOnlyList<CustomerHistoryEntry>? entries = null)
    {
        _bookings.Setup(x => x.GetDecisionSnapshotAsync(BookingId, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("snapshot")).ReturnsAsync(snapshot);
        _history.Setup(x => x.GetEntriesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("history")).ReturnsAsync(entries ?? []);
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("client"))
            .ReturnsAsync(TadssCallResult<TadssDecisionResultDto>.Success(Approved(), 200, "corr"));
        var monitor = new Mock<IOptionsMonitor<TadssSettings>>();
        monitor.Setup(x => x.CurrentValue).Returns(_settings);
        var paymentMonitor = new Mock<IOptionsMonitor<BookingPaymentSettings>>();
        paymentMonitor.Setup(x => x.CurrentValue).Returns(_paymentSettings);
        return new BookingDepositService(_bookings.Object, _history.Object, _client.Object, monitor.Object, paymentMonitor.Object, _logger);
    }

    private void CaptureRequestInto(List<TadssDecisionRequestDto> sent) =>
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>()))
            .Callback<TadssDecisionRequestDto, Guid, CancellationToken>((r, _, _) => sent.Add(r))
            .ReturnsAsync(TadssCallResult<TadssDecisionResultDto>.Success(Approved(), 200, "corr"));

    private static string Json(TadssDecisionRequestDto request) => JsonSerializer.Serialize(request, TadssJson.Options);

    [Fact]
    public async Task EvaluateAsync_WhenBookingExists_CallsClientOnceWithMapperRequestAndBookingIdAsKey()
    {
        var snapshot = Snapshot();
        var service = CreateService(snapshot);

        await service.EvaluateAsync(BookingId);

        var expected = TadssDecisionRequestMapper.Map(snapshot, new CustomerHistorySummary(0, 0, 0, 0, 0, null), _settings);
        _client.Verify(x => x.EvaluateAsync(
            It.Is<TadssDecisionRequestDto>(r => Json(r) == Json(expected)), BookingId, It.IsAny<CancellationToken>()), Times.Once);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EvaluateAsync_WhenClientSucceeds_ReturnsTheClientResultUntouched()
    {
        var service = CreateService(Snapshot());
        var expected = TadssCallResult<TadssDecisionResultDto>.Success(Approved(), 200, "corr-1");
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await service.EvaluateAsync(BookingId);

        Assert.Same(expected, result);
    }

    [Theory]
    [InlineData(TadssFailureKind.Timeout)]
    [InlineData(TadssFailureKind.Network)]
    [InlineData(TadssFailureKind.Http)]
    [InlineData(TadssFailureKind.InvalidResponse)]
    public async Task EvaluateAsync_WhenClientFails_ReturnsTheFailureWithoutSwallowingIt(TadssFailureKind kind)
    {
        var service = CreateService(Snapshot());
        var failure = TadssCallResult<TadssDecisionResultDto>.Failure(
            kind, "corr-2", kind == TadssFailureKind.Http ? 503 : null, "PERSISTENCE_UNAVAILABLE", TimeSpan.FromSeconds(5));
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>())).ReturnsAsync(failure);

        var result = await service.EvaluateAsync(BookingId);

        Assert.Same(failure, result);
        Assert.False(result.IsSuccess);
        Assert.Equal(kind, result.FailureKind);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCalled_ReadsDatabaseBeforeCallingTadss()
    {
        var service = CreateService(Snapshot());

        await service.EvaluateAsync(BookingId);

        Assert.Equal(["snapshot", "history", "client"], _calls);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCalled_AsksHistoryForTheCustomerExcludingThisBooking()
    {
        var service = CreateService(Snapshot());

        await service.EvaluateAsync(BookingId);

        _history.Verify(x => x.GetEntriesAsync(CustomerId, BookingId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_WhenHistoryHasEntries_CountsThemAsOfBookingCreationNotNow()
    {
        var entries = new[]
        {
            // hủy hết hạn thanh toán 1 phút trước khi đơn được tạo thì tính, 1 phút sau thì không
            new CustomerHistoryEntry(BookingStatus.CANCELLED, BookingCancelReason.PAYMENT_EXPIRED, CreatedAt.AddMinutes(-1), null, null, false, null),
            new CustomerHistoryEntry(BookingStatus.CANCELLED, BookingCancelReason.PAYMENT_EXPIRED, CreatedAt.AddMinutes(1), null, null, false, null),
            new CustomerHistoryEntry(BookingStatus.COMPLETED, null, null, new DateOnly(2026, 11, 1), new TimeOnly(20, 0), false, null)
        };
        var service = CreateService(Snapshot(), entries);
        var sent = new List<TadssDecisionRequestDto>();
        CaptureRequestInto(sent);

        await service.EvaluateAsync(BookingId);

        var history = Assert.Single(sent).Customer.History!;
        Assert.Equal(1, history.PaymentExpiredCount);
        Assert.Equal(1, history.CompletedCount);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCalledTwiceWithSameData_SendsIdenticalRequests()
    {
        var service = CreateService(Snapshot());
        var sent = new List<TadssDecisionRequestDto>();
        CaptureRequestInto(sent);

        await service.EvaluateAsync(BookingId);
        await service.EvaluateAsync(BookingId);

        Assert.Equal(Json(sent[0]), Json(sent[1]));
    }

    [Fact]
    public async Task EvaluateAsync_WhenSettingsChange_UsesTheLatestBranchTimeZone()
    {
        var service = CreateService(Snapshot());
        var sent = new List<TadssDecisionRequestDto>();
        CaptureRequestInto(sent);

        _settings.BranchTimeZone = "Asia/Tokyo";
        await service.EvaluateAsync(BookingId);

        Assert.Equal("Asia/Tokyo", Assert.Single(sent).BranchTimeZone);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCancellationTokenGiven_PassesItToRepositoriesAndClient()
    {
        var service = CreateService(Snapshot());
        using var cts = new CancellationTokenSource();

        await service.EvaluateAsync(BookingId, cts.Token);

        _bookings.Verify(x => x.GetDecisionSnapshotAsync(BookingId, cts.Token), Times.Once);
        _history.Verify(x => x.GetEntriesAsync(CustomerId, BookingId, cts.Token), Times.Once);
        _client.Verify(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, cts.Token), Times.Once);
    }

    [Fact]
    public async Task EvaluateAsync_WhenCallerCancelsInClient_PropagatesCancellation()
    {
        var service = CreateService(Snapshot());
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.EvaluateAsync(BookingId));
    }

    [Fact]
    public async Task EvaluateAsync_WhenSnapshotIsMissing_ThrowsProgrammingErrorAndNeverCallsTadss()
    {
        var service = CreateService(snapshot: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(BookingId));

        Assert.Contains(BookingId.ToString(), ex.Message);
        _history.VerifyNoOtherCalls();
        _client.VerifyNoOtherCalls();
    }

    // ───────────────────────── DecideAsync (D2, D5, D6) ─────────────────────────

    private static TadssDecisionResultDto Result(TadssDecisionStatus status, TadssDepositDto? deposit = null, DateTimeOffset? approvalExpiresAt = null) => new()
    {
        DecisionId = Guid.NewGuid(), ReservationId = BookingId, Status = status, Deposit = deposit,
        StrategyName = "DefaultDecisionStrategy", StrategyVersion = "2.1.0", ExecutionType = "AUTOMATED", FallbackUsed = false,
        ReasonCode = "TIER_DEPOSIT", Reasons = ["lý do"], Recommendations = [], ApprovalExpiresAt = approvalExpiresAt, EvaluatedAt = CreatedAt
    };

    private void ClientReturns(TadssCallResult<TadssDecisionResultDto> result) =>
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>())).ReturnsAsync(result);

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenFlagIsOff_NeverReadsDatabaseNorCallsTadssAndFallsBackToFullPayment()
    {
        _settings.EnableTadssDecision = false;
        var service = CreateService(Snapshot());

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Equal(228_000m, decision.DepositAmount);
        _client.VerifyNoOtherCalls();
        _bookings.VerifyNoOtherCalls();
        _history.VerifyNoOtherCalls();
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-24")]
    public async Task DecideAsync_WhenFlagIsOffAndOrderBelowMinimum_ConfirmsWithoutOnlinePaymentAndLogsWarning()
    {
        _settings.EnableTadssDecision = false;
        var service = CreateService(Snapshot());

        var decision = await service.DecideAsync(BookingId, 3_000m);

        Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, decision.Branch);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(BookingId.ToString(), entry.Message);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Catalog", "BE-IT-02")]
    public async Task DecideAsync_WhenFlagIsOnAndTadssApproves_CallsTadssOnceAndReturnsTadssDeposit()
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        var approved = Result(TadssDecisionStatus.APPROVED, new TadssDepositDto { Rate = 0.5m, Amount = 114_000m, Currency = "VND" });
        ClientReturns(TadssCallResult<TadssDecisionResultDto>.Success(approved, 200, "corr"));

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.TadssDeposit, decision.Branch);
        Assert.Equal(approved.DecisionId, decision.TadssDecisionId);
        Assert.Equal(114_000m, decision.DepositAmount);
        _client.Verify(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-03")]
    public async Task DecideAsync_WhenTadssNeedsApproval_ReturnsNeedApprovalWithUtcDeadlineAndLogsInformation()
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        ClientReturns(TadssCallResult<TadssDecisionResultDto>.Success(
            Result(TadssDecisionStatus.NEED_APPROVAL, approvalExpiresAt: DateTimeOffset.Parse("2026-11-18T10:30:00+07:00")), 200, "corr"));

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.NeedApproval, decision.Branch);
        Assert.Equal(new DateTime(2026, 11, 18, 3, 30, 0, DateTimeKind.Utc), decision.ApprovalExpiresAtUtc);
        Assert.Equal(LogLevel.Information, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-17")]
    public async Task DecideAsync_WhenTadssRejects_ReturnsRejected()
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        ClientReturns(TadssCallResult<TadssDecisionResultDto>.Success(Result(TadssDecisionStatus.REJECTED), 200, "corr"));

        var decision = await service.DecideAsync(BookingId, 0m);

        Assert.Equal(DepositBranch.Rejected, decision.Branch);
    }

    [Theory]
    [InlineData(TadssFailureKind.Timeout, null)]
    [InlineData(TadssFailureKind.Network, null)]
    [InlineData(TadssFailureKind.Http, 503)]
    [InlineData(TadssFailureKind.Http, 401)]
    [InlineData(TadssFailureKind.InvalidResponse, 200)]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenTadssFails_FallsBackToFullPaymentAndLogsWarning(TadssFailureKind kind, int? http)
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        ClientReturns(TadssCallResult<TadssDecisionResultDto>.Failure(kind, "corr-9", http));

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Null(decision.TadssDecisionId);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("corr-9", entry.Message);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(409)]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenTadssReturns400Or409_LogsErrorAndStillFallsBack(int http)
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        ClientReturns(TadssCallResult<TadssDecisionResultDto>.Failure(TadssFailureKind.Http, "corr", http, "IDEMPOTENCY_KEY_REUSE"));

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(LogLevel.Error, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-19")]
    public async Task DecideAsync_ReadsTheFlagOnEveryCall_SoChangesTakeEffectWithoutRestart()
    {
        var service = CreateService(Snapshot());

        _settings.EnableTadssDecision = false;
        await service.DecideAsync(BookingId, 228_000m);
        _client.Verify(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        _settings.EnableTadssDecision = true;
        await service.DecideAsync(BookingId, 228_000m);
        _client.Verify(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);

        _settings.EnableTadssDecision = false;
        await service.DecideAsync(BookingId, 228_000m);
        _client.Verify(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DecideAsync_ReadsTheMinimumPaymentAmountOnEveryCall()
    {
        _settings.EnableTadssDecision = false;
        var service = CreateService(Snapshot());

        _paymentSettings.MinOnlinePaymentAmount = 5_000m;
        Assert.Equal(DepositBranch.FullPaymentFallback, (await service.DecideAsync(BookingId, 6_000m)).Branch);

        _paymentSettings.MinOnlinePaymentAmount = 10_000m;
        Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, (await service.DecideAsync(BookingId, 6_000m)).Branch);
    }

    [Fact]
    public async Task DecideAsync_WhenFlagIsOn_PassesTheCancellationTokenToRepositoriesAndClient()
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        using var cts = new CancellationTokenSource();

        await service.DecideAsync(BookingId, 228_000m, cts.Token);

        _bookings.Verify(x => x.GetDecisionSnapshotAsync(BookingId, cts.Token), Times.Once);
        _client.Verify(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, cts.Token), Times.Once);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenCallerCancelsBeforeStart_ThrowsOperationCanceledAndLogsNothing()
    {
        // K-06: hợp đồng cho caller khác; CreateOnlineAsync truyền CancellationToken.None (D6)
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>()))
            .Returns<TadssDecisionRequestDto, Guid, CancellationToken>((_, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(TadssCallResult<TadssDecisionResultDto>.Failure(TadssFailureKind.Timeout, "corr"));
            });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.DecideAsync(BookingId, 228_000m, cts.Token));

        Assert.Empty(_logger.Entries);
    }

    // ── F2: lỗi khi dựng request (sau transaction 1) không được làm hỏng đơn: dự phòng 100 %, log Error kèm exception ──

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenFlagIsOnButSnapshotIsMissing_FallsBackToFullPaymentAndLogsErrorWithException()
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(snapshot: null);

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Equal(228_000m, decision.DepositAmount);
        Assert.Null(decision.TadssDecisionId);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains(BookingId.ToString(), entry.Message);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Catalog", "BE-IT-04")]
    public async Task DecideAsync_WhenReadingHistoryFails_FallsBackToFullPaymentWithoutCallingTadss()
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        var dbError = new TimeoutException("db timeout");
        _history.Setup(x => x.GetEntriesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(dbError);

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Same(dbError, Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error).Exception);
        _client.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Catalog", "BE-IT-24")]
    public async Task DecideAsync_WhenBuildFailsOnSmallOrder_ConfirmsWithoutOnlinePaymentAndKeepsErrorLevel()
    {
        // Dự phòng của đơn dưới mức tối thiểu là a′ (Warning) nhưng nguyên nhân là lỗi của BE nên giữ Error
        _settings.EnableTadssDecision = true;
        var service = CreateService(snapshot: null);

        var decision = await service.DecideAsync(BookingId, 3_000m);

        Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Equal(LogLevel.Error, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task DecideAsync_WhenClientThrowsUnexpectedly_FallsBackInsteadOfThrowing()
    {
        // Client vốn không ném cho lỗi TADSS; nếu một lỗi lập trình lọt ra vẫn không được làm hỏng đơn đã commit
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        _client.Setup(x => x.EvaluateAsync(It.IsAny<TadssDecisionRequestDto>(), BookingId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NullReferenceException());

        var decision = await service.DecideAsync(BookingId, 228_000m);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.IsType<NullReferenceException>(Assert.Single(_logger.Entries).Exception);
    }

    [Fact]
    public async Task DecideAsync_WhenCancelledWhileReadingDatabase_StillPropagatesCancellation()
    {
        // Hủy chủ động không phải sự cố: không được nuốt thành dự phòng
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());
        _history.Setup(x => x.GetEntriesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.DecideAsync(BookingId, 228_000m));
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSnapshotIsMissing_StillThrowsForOtherCallers()
    {
        // Chỉ DecideAsync (luồng đặt sân) chuyển lỗi thành dự phòng; EvaluateAsync giữ hợp đồng ném lỗi lập trình
        _settings.EnableTadssDecision = true;
        var service = CreateService(snapshot: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(BookingId));
    }

    [Fact]
    public async Task DecideAsync_NeverWritesToTheDatabase()
    {
        _settings.EnableTadssDecision = true;
        var service = CreateService(Snapshot());

        await service.DecideAsync(BookingId, 228_000m);

        // chỉ đọc snapshot; không hàm ghi nào của repository được gọi
        _bookings.Verify(x => x.GetDecisionSnapshotAsync(BookingId, It.IsAny<CancellationToken>()), Times.Once);
        _bookings.VerifyNoOtherCalls();
    }
}
