using System.Text.Json;
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
        return new BookingDepositService(_bookings.Object, _history.Object, _client.Object, monitor.Object);
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
}
