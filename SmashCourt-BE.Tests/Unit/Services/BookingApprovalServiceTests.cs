using SmashCourt_BE.Models.Deposit;
using Microsoft.Extensions.Logging;
using Moq;
using SmashCourt_BE.Common;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Services;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Services;

/// <summary>
/// Phase 6A-3 (spec 7.7, DEPOSIT_APPROVAL_API 4): hàng chờ, duyệt, từ chối, đồng bộ Q9. Đơn mẫu giữ trong bộ nhớ; các hàm ghi giả lập đổi
/// trạng thái như DB nên "đọc lại sau khi ghi" phản ánh đúng kết quả.
/// </summary>
[Trait("Category", TestCategories.Booking)]
public class BookingApprovalServiceTests
{
    private static readonly DateTime Now = BookingApprovalServiceTestBuilder.DefaultNow.UtcDateTime; // 09:30 giờ VN 18/11
    private static readonly DateOnly PlayDate = new(2026, 11, 20);
    private static readonly DateTime PlayStartUtc = DateTimeHelper.ToUtcFromVietnam(PlayDate, new TimeOnly(18, 0));
    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d003");
    private const string Reason = "Khách quen, đã gọi xác nhận";
    private const string ManagerRole = "BRANCH_MANAGER";

    private sealed class Scenario
    {
        public BookingApprovalServiceTestBuilder Builder { get; } = new();
        public Guid ActorId { get; } = Guid.NewGuid();
        public Booking State { get; set; }
        public bool Missing { get; set; }
        public bool GateWins { get; set; } = true;
        public bool ActorInBranch { get; set; } = true;
        public List<TadssOverrideRequestDto> OverrideRequests { get; } = [];
        public List<string> OverrideActors { get; } = [];
        public List<CancellationToken> OverrideTokens { get; } = [];
        public TadssCallResult<TadssOverrideResponseDto> OverrideResult { get; set; }
        public TadssCallResult<TadssDecisionDetailDto>? DetailResult { get; set; }
        public TimeSpan TadssDelay { get; set; } = TimeSpan.Zero;
        public List<BookingApprovalResolvedNotificationDto> Resolved { get; } = [];
        public List<Guid?> ResolvedCustomers { get; } = [];
        public List<PendingApprovalQueueChangedNotificationDto> QueueChanges { get; } = [];
        public List<(string Event, BookingNotificationDto Payload, bool TimeGrid)> BookingEvents { get; } = [];

        public Scenario()
        {
            State = NewPendingBooking();
            OverrideResult = Success(ApproveResponse(0.3m, 60_000m));
            var b = Builder;

            b.BookingRepository.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => Missing || id != State.Id ? null : State);
            b.UserBranchRepository.Setup(x => x.IsUserInBranchAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync(() => ActorInBranch);
            b.Tadss.Setup(x => x.OverrideAsync(It.IsAny<Guid>(), It.IsAny<TadssOverrideRequestDto>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, TadssOverrideRequestDto request, string actor, CancellationToken token) =>
                {
                    OverrideRequests.Add(request);
                    OverrideActors.Add(actor);
                    OverrideTokens.Add(token);
                    Builder.Time.Advance(TadssDelay);
                    return OverrideResult;
                });
            b.Tadss.Setup(x => x.GetDecisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => DetailResult ?? TadssCallResult<TadssDecisionDetailDto>.Failure(TadssFailureKind.Timeout, "corr-get"));

            b.BookingRepository.Setup(x => x.ApproveWithDepositAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                    It.IsAny<PaymentHoldWindow>(), It.IsAny<DateTime>()))
                .ReturnsAsync((Guid _, Guid? by, string note, decimal rate, decimal amount, PaymentHoldWindow window, DateTime at) =>
                {
                    if (!GateWins) { LoseRace(); return false; }
                    Resolve(BookingStatus.PENDING, ApprovalResult.APPROVED, by, note, at);
                    State.Invoice!.DepositRate = rate;
                    State.Invoice.DepositAmount = amount;
                    State.Invoice.DepositSource = DepositSource.MANAGER;
                    State.Invoice.ExpiresAt = window.ExpiresAt;
                    State.Invoice.HoldDeadline = window.HoldDeadline;
                    return true;
                });
            b.BookingRepository.Setup(x => x.ApproveWithoutDepositAsync(
                    It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
                .ReturnsAsync((Guid _, Guid? by, string note, string hash, DateTime tokenExpires, DateTime at) =>
                {
                    if (!GateWins) { LoseRace(); return false; }
                    Resolve(BookingStatus.CONFIRMED, ApprovalResult.APPROVED, by, note, at);
                    State.CancelTokenHash = hash;
                    State.CancelTokenExpiresAt = tokenExpires;
                    State.Invoice!.DepositRate = 0m;
                    State.Invoice.DepositAmount = 0m;
                    State.Invoice.DepositSource = DepositSource.MANAGER;
                    State.Invoice.PaymentTiming = PaymentTiming.POSTPAID;
                    State.Invoice.ExpiresAt = null;
                    return true;
                });
            b.BookingRepository.Setup(x => x.RejectByManagerAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ReturnsAsync((Guid _, Guid? by, string note, DateTime at) =>
                {
                    if (!GateWins) { LoseRace(); return false; }
                    Resolve(BookingStatus.CANCELLED, ApprovalResult.REJECTED, by, note, at);
                    State.CancelReason = BookingCancelReason.APPROVAL_REJECTED;
                    State.CancelSource = CancelSourceEnum.STAFF;
                    State.CancelledAt = at;
                    State.CancelledBy = by;
                    State.Invoice!.PaymentStatus = InvoicePaymentStatus.EXPIRED;
                    return true;
                });

            b.Broadcast.Setup(x => x.BroadcastBookingApprovalResolvedAsync(It.IsAny<BookingApprovalResolvedNotificationDto>(), It.IsAny<Guid?>()))
                .Callback((BookingApprovalResolvedNotificationDto payload, Guid? customer) => { Resolved.Add(payload); ResolvedCustomers.Add(customer); })
                .Returns(Task.CompletedTask);
            b.Broadcast.Setup(x => x.BroadcastPendingApprovalQueueChangedAsync(It.IsAny<PendingApprovalQueueChangedNotificationDto>()))
                .Callback((PendingApprovalQueueChangedNotificationDto payload) => QueueChanges.Add(payload))
                .Returns(Task.CompletedTask);
            b.Broadcast.Setup(x => x.BroadcastBookingEventAsync(It.IsAny<string>(), It.IsAny<BookingNotificationDto>(), It.IsAny<Booking>(), It.IsAny<bool>()))
                .Callback((string name, BookingNotificationDto payload, Booking _, bool grid) => BookingEvents.Add((name, payload, grid)))
                .Returns(Task.CompletedTask);
        }

        private void Resolve(BookingStatus status, ApprovalResult result, Guid? by, string note, DateTime at)
        {
            State.Status = status;
            State.ApprovalResult = result;
            State.ApprovalResolvedBy = by;
            State.ApprovalResolvedAt = at;
            State.ApprovalResolutionNote = note;
        }

        // Một request khác (Quản lý khác) vừa duyệt trước
        private void LoseRace()
        {
            State.Status = BookingStatus.PENDING;
            State.ApprovalResult = ApprovalResult.APPROVED;
            State.ApprovalResolvedAt = Now;
        }

        public Task<BookingApprovalResultDto> ApproveAsync(decimal rate = 0.3m, string role = ManagerRole) =>
            Builder.Build().ApproveAsync(State.Id, new ApproveBookingDto { DepositRate = rate, Reason = Reason }, ActorId, role);

        public Task<BookingApprovalResultDto> RejectAsync(string role = ManagerRole) =>
            Builder.Build().RejectAsync(State.Id, new RejectBookingDto { Reason = Reason }, ActorId, role);

        public void VerifyNoWrite()
        {
            var repo = Builder.BookingRepository;
            repo.Verify(x => x.ApproveWithDepositAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<PaymentHoldWindow>(), It.IsAny<DateTime>()), Times.Never);
            repo.Verify(x => x.ApproveWithoutDepositAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
            repo.Verify(x => x.RejectByManagerAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        public void VerifyNothingAnnounced()
        {
            Assert.Empty(Resolved);
            Assert.Empty(QueueChanges);
            Assert.Empty(BookingEvents);
            Assert.Empty(Builder.Email.Sent);
            Builder.SlotInterestNotifier.Verify(x => x.NotifyReleasedSlotsAsync(It.IsAny<Booking>()), Times.Never);
            Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
        }

        public void VerifyTadssNotCalled() =>
            Builder.Tadss.Verify(x => x.OverrideAsync(It.IsAny<Guid>(), It.IsAny<TadssOverrideRequestDto>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Booking NewPendingBooking()
    {
        var booking = TestDataFactory.CreateBooking(status: BookingStatus.PENDING_APPROVAL, bookingDate: PlayDate);
        booking.Source = BookingSource.ONLINE;
        booking.BookingCode = "BK-20261118-000123";
        booking.TadssDecisionId = DecisionId;
        booking.ApprovalExpiresAt = Now.AddMinutes(20);
        booking.ApprovalReasonCode = "LARGE_ORDER_REVIEW";
        booking.ApprovalReasons = ["Đơn từ 2.000.000 đ cần duyệt."];
        booking.Note = "Cần mượn vợt";
        booking.Customer = new User
        {
            Id = booking.CustomerId!.Value, Email = "khach@test.local", FullName = "Nguyễn Văn An", Phone = "0901234567",
            CustomerLoyalty = new CustomerLoyalty { Tier = new LoyaltyTier { Name = "Vàng" } }
        };
        booking.Branch = new Branch { Id = booking.BranchId, Name = "SmashCourt Thủ Đức", Address = "1 Võ Văn Ngân", Phone = "0281234567" };
        var court = new Court { Id = Guid.NewGuid(), BranchId = booking.BranchId, Name = "Sân 3", CourtTypeId = Guid.NewGuid() };
        booking.BookingCourts.Add(new BookingCourt
        {
            CourtId = court.Id, Court = court, Date = PlayDate, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(20, 0), IsActive = true
        });
        booking.Invoice = new Invoice
        {
            Id = Guid.NewGuid(), BookingId = booking.Id, CourtFee = 200_000m, FinalTotal = 200_000m,
            PaymentStatus = InvoicePaymentStatus.UNPAID, PaymentTiming = PaymentTiming.PREPAID, HoldDeadline = Now.AddMinutes(-10)
        };
        return booking;
    }

    private static TadssCallResult<TadssOverrideResponseDto> Success(TadssOverrideResponseDto value) =>
        TadssCallResult<TadssOverrideResponseDto>.Success(value, 200, "corr-1");

    private static TadssCallResult<TadssOverrideResponseDto> Failure(int? status, string? code = null, TadssFailureKind kind = TadssFailureKind.Http) =>
        TadssCallResult<TadssOverrideResponseDto>.Failure(kind, "corr-1", status, code);

    private static TadssOverrideResponseDto ApproveResponse(decimal rate, decimal amount, string currency = "VND", string? actor = null) => new()
    {
        DecisionId = DecisionId, Action = TadssOverrideAction.APPROVE,
        Deposit = new TadssDepositDto { Rate = rate, Amount = amount, Currency = currency },
        ActorId = actor ?? Guid.NewGuid().ToString(), Reason = Reason, OverriddenAt = Now
    };

    // TADSS trả lại đúng lý do đã nhận; lý do khác chỉ xuất hiện khi đồng bộ ghi đè của người khác
    private static TadssOverrideResponseDto RejectResponse(string? actor = null, string reason = Reason) => new()
    {
        DecisionId = DecisionId, Action = TadssOverrideAction.REJECT, ActorId = actor ?? Guid.NewGuid().ToString(), Reason = reason,
        OverriddenAt = Now
    };

    // ───────────────────────── Hàng chờ (Task 9) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task Queue_ResolvesBranchScopeAndMapsEveryField_WithoutCallingTadss()
    {
        var scenario = new Scenario();
        var b = scenario.Builder;
        var booking = scenario.State;
        b.BranchScope.Setup(x => x.ResolveOptionalBranchIdAsync(null, scenario.ActorId, UserRole.BRANCH_MANAGER)).ReturnsAsync(booking.BranchId);
        b.BookingRepository.Setup(x => x.GetPendingApprovalsAsync(booking.BranchId, Now, 2, 5))
            .ReturnsAsync(new PagedResult<Booking> { Items = [booking], Page = 2, PageSize = 5, TotalItems = 6 });

        var page = await b.Build().GetPendingApprovalsAsync(new PendingApprovalListQuery { Page = 2, PageSize = 5 }, scenario.ActorId, ManagerRole);

        Assert.Equal(6, page.TotalItems);
        Assert.Equal(2, page.Page);
        var item = Assert.Single(page.Items);
        Assert.Equal(booking.Id, item.BookingId);
        Assert.Equal("BK-20261118-000123", item.BookingCode);
        Assert.Equal(booking.BranchId, item.BranchId);
        Assert.Equal("SmashCourt Thủ Đức", item.BranchName);
        Assert.Equal(booking.CustomerId, item.CustomerId);
        Assert.Equal("Nguyễn Văn An", item.CustomerName);
        Assert.Equal("0901234567", item.CustomerPhone);
        Assert.False(item.IsGuest);
        Assert.Equal("Vàng", item.LoyaltyTierName);
        Assert.Equal(PlayDate, item.BookingDate);
        Assert.Equal(new TimeSpan(18, 0, 0), item.StartTime);
        Assert.Equal(new TimeSpan(20, 0, 0), item.EndTime);
        Assert.Equal("Sân 3", Assert.Single(item.Courts).CourtName);
        Assert.Equal(200_000m, item.FinalTotal);
        Assert.Equal("LARGE_ORDER_REVIEW", item.ApprovalReasonCode);
        Assert.Equal(["Đơn từ 2.000.000 đ cần duyệt."], item.ApprovalReasons);
        Assert.Equal(Now.AddMinutes(20), item.ApprovalExpiresAt);
        Assert.Equal("Cần mượn vợt", item.Note);
        b.Tadss.VerifyNoOtherCalls();
    }

    [Fact]
    [Trait("Catalog", "BE-IT-05")]
    public async Task Queue_WhenScopeIsForbidden_DoesNotQuery()
    {
        var scenario = new Scenario();
        var b = scenario.Builder;
        var other = Guid.NewGuid();
        b.BranchScope.Setup(x => x.ResolveOptionalBranchIdAsync(other, scenario.ActorId, UserRole.BRANCH_MANAGER))
            .ThrowsAsync(new AppException(403, "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden));

        var error = await Assert.ThrowsAsync<AppException>(() =>
            b.Build().GetPendingApprovalsAsync(new PendingApprovalListQuery { BranchId = other }, scenario.ActorId, ManagerRole));

        error.ShouldBeAppException(403, ErrorCodes.Forbidden);
        b.BookingRepository.Verify(x => x.GetPendingApprovalsAsync(It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    // ───────────────────────── Thứ tự kiểm trước TADSS (BE-IT-12) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-12")]
    public async Task Approve_BookingMissing_Is404WithoutTadss()
    {
        var scenario = new Scenario { Missing = true };

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync())).ShouldBeAppException(404, ErrorCodes.NotFound);
        scenario.VerifyTadssNotCalled();
    }

    [Fact]
    [Trait("Catalog", "BE-IT-11")]
    public async Task Approve_ManagerOfAnotherBranch_Is403WithoutTadss()
    {
        var scenario = new Scenario { ActorInBranch = false };

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync())).ShouldBeAppException(403, ErrorCodes.Forbidden);
        scenario.Builder.UserBranchRepository.Verify(x => x.IsUserInBranchAsync(scenario.ActorId, scenario.State.BranchId), Times.Once);
        scenario.VerifyTadssNotCalled();
        scenario.VerifyNoWrite();
    }

    [Fact]
    [Trait("Catalog", "BE-IT-11")]
    public async Task Approve_Owner_SkipsBranchCheck()
    {
        var scenario = new Scenario { ActorInBranch = false };

        await scenario.ApproveAsync(role: "OWNER");

        scenario.Builder.UserBranchRepository.Verify(x => x.IsUserInBranchAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    public static TheoryData<string, string> Conflicts => new()
    {
        { "resolved", ErrorCodes.ApprovalAlreadyResolved },
        { "expired", ErrorCodes.ApprovalExpired },
        { "walk-in", ErrorCodes.BookingNotPendingApproval },
        { "no-decision", ErrorCodes.ApprovalDecisionNotFound }
    };

    private static void Apply(Booking booking, string state)
    {
        switch (state)
        {
            case "resolved": booking.Status = BookingStatus.PENDING; booking.ApprovalResult = ApprovalResult.APPROVED; booking.ApprovalResolvedAt = Now; break;
            case "expired": booking.ApprovalExpiresAt = Now; break;
            case "walk-in": booking.Status = BookingStatus.CONFIRMED; booking.Source = BookingSource.WALK_IN; break;
            case "no-decision": booking.TadssDecisionId = null; break; // DB thật cấm: chỉ kiểm được ở đây
        }
    }

    [Theory]
    [MemberData(nameof(Conflicts))]
    [Trait("Catalog", "BE-IT-12")]
    public async Task Approve_NotApprovable_Is409WithoutTadss(string state, string code)
    {
        var scenario = new Scenario();
        Apply(scenario.State, state);

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync())).ShouldBeAppException(409, code);
        scenario.VerifyTadssNotCalled();
        scenario.VerifyNoWrite();
    }

    [Theory]
    [MemberData(nameof(Conflicts))]
    [Trait("Catalog", "BE-IT-12")]
    public async Task Reject_NotApprovable_Is409WithoutTadss(string state, string code)
    {
        var scenario = new Scenario();
        Apply(scenario.State, state);

        (await Assert.ThrowsAsync<AppException>(() => scenario.RejectAsync())).ShouldBeAppException(409, code);
        scenario.VerifyTadssNotCalled();
        scenario.VerifyNoWrite();
    }

    [Theory]
    [InlineData(200_000, "0.02")]    // 4.000 đ
    [InlineData(200_000, "0.0001")]  // 20 đ
    [InlineData(166_630, "0.03")]    // 4.998,9 ⇒ 4.999 đ
    [Trait("Catalog", "BE-UT-05")]
    public async Task Approve_AmountAboveZeroButBelowMinimum_Is400OnDepositRateWithoutTadss(decimal total, string rate)
    {
        var scenario = new Scenario();
        scenario.State.Invoice!.FinalTotal = total;

        var error = await Assert.ThrowsAsync<AppException>(() =>
            scenario.ApproveAsync(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)));

        error.ShouldBeAppException(400, ErrorCodes.ValidationError);
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", error.Message);
        Assert.Equal(["Số tiền cọc tối thiểu 5.000 đ, hoặc chọn 0 %"], error.Errors!["DepositRate"]);
        scenario.VerifyTadssNotCalled();
    }

    [Theory]
    [InlineData(200_000, "0")]       // 0 đ: miễn cọc hợp lệ (Q4)
    [InlineData(200_000, "0.025")]   // đúng 5.000 đ
    [InlineData(166_650, "0.03")]    // 4.999,5 làm tròn xa số 0 ⇒ 5.000 đ (cùng công thức TADSS)
    [InlineData(0, "0.5")]           // đơn 0 đ: mọi tỷ lệ ra 0
    [Trait("Catalog", "BE-UT-05")]
    public async Task Approve_AmountZeroOrAtLeastMinimum_CallsTadss(decimal total, string rate)
    {
        var scenario = new Scenario();
        scenario.State.Invoice!.FinalTotal = total;
        var parsedRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture);
        scenario.OverrideResult = Success(ApproveResponse(parsedRate, Math.Round(total * parsedRate, 0, MidpointRounding.AwayFromZero)));

        await scenario.ApproveAsync(parsedRate);

        Assert.Single(scenario.OverrideRequests);
    }

    [Fact]
    public async Task Approve_MinimumComesFromConfiguration()
    {
        var scenario = new Scenario();
        scenario.Builder.PaymentSettings.MinOnlinePaymentAmount = 10_000;

        var error = await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync(0.025m)); // 5.000 đ < 10.000 đ

        Assert.Equal(["Số tiền cọc tối thiểu 10.000 đ, hoặc chọn 0 %"], error.Errors!["DepositRate"]);
    }

    // ───────────────────────── Duyệt cọc > 0 (Task 10) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-06")]
    public async Task Approve_SendsApproveOverrideWithActorFromJwtAndAnUncancellableToken()
    {
        var scenario = new Scenario();

        await scenario.ApproveAsync(0.3m);

        var request = Assert.Single(scenario.OverrideRequests);
        Assert.Equal(TadssOverrideAction.APPROVE, request.Action);
        Assert.Equal(0.3m, request.Deposit!.Rate);
        Assert.Null(request.Deposit.Amount);
        Assert.Equal(Reason, request.Reason);
        Assert.Equal(scenario.ActorId.ToString(), Assert.Single(scenario.OverrideActors));
        Assert.False(Assert.Single(scenario.OverrideTokens).CanBeCanceled); // D14: Quản lý đóng tab vẫn phải ghi theo TADSS
        scenario.Builder.Tadss.Verify(x => x.OverrideAsync(DecisionId, It.IsAny<TadssOverrideRequestDto>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task Approve_Deposit_WritesTadssAmountWithWindowFromTheMomentAfterTadss()
    {
        var scenario = new Scenario { TadssDelay = TimeSpan.FromMinutes(1) }; // giờ duyệt = sau khi TADSS trả lời
        scenario.OverrideResult = Success(ApproveResponse(0.3m, 60_000m));
        var decidedAt = Now.AddMinutes(1);

        var result = await scenario.ApproveAsync(0.3m);

        scenario.Builder.BookingRepository.Verify(x => x.ApproveWithDepositAsync(
            scenario.State.Id, scenario.ActorId, Reason, 0.3m, 60_000m,
            new PaymentHoldWindow(decidedAt.AddMinutes(15), decidedAt.AddMinutes(15)), decidedAt), Times.Once);
        Assert.Equal("APPROVED", result.ApprovalResult);
        Assert.Equal("PENDING", result.Status);
        Assert.Equal(200_000m, result.FinalTotal);
        Assert.Equal(0.3m, result.DepositRate);
        Assert.Equal(60_000m, result.DepositAmount);
        Assert.Equal(140_000m, result.RemainingAmount);
        Assert.Equal("MANAGER", result.DepositSource);
        Assert.Equal(decidedAt.AddMinutes(15), result.ExpiresAt);
        Assert.Equal(scenario.ActorId, result.ResolvedBy);
        Assert.Equal(decidedAt, result.ResolvedAt);
        scenario.Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never); // tăng khi IPN
        scenario.Builder.UnitOfWork.Verify(x => x.ClearChangeTracker(), Times.AtLeastOnce);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task Approve_PlayStartsWithinTheWindow_DeadlineIsPlayStart()
    {
        var scenario = new Scenario();
        scenario.State.BookingCourts.First().Date = DateOnly.FromDateTime(DateTimeHelper.ToVietnamTime(Now));
        scenario.State.BookingCourts.First().StartTime = TimeOnly.FromDateTime(DateTimeHelper.ToVietnamTime(Now.AddMinutes(10)));
        var playStart = Now.AddMinutes(10);

        await scenario.ApproveAsync(0.3m);

        scenario.Builder.BookingRepository.Verify(x => x.ApproveWithDepositAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            new PaymentHoldWindow(playStart, playStart), Now), Times.Once);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task Approve_TadssAmountExactlyAtMinimum_StaysPendingForOnlinePayment()
    {
        var scenario = new Scenario { OverrideResult = Success(ApproveResponse(0.025m, 5_000m)) };

        var result = await scenario.ApproveAsync(0.025m);

        Assert.Equal("PENDING", result.Status);
        Assert.Equal(5_000m, result.DepositAmount);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-18")]
    public async Task Approve_Deposit_AnnouncesAfterCommitAndEmailsTheDepositDeadline()
    {
        var scenario = new Scenario();

        await scenario.ApproveAsync(0.3m);

        var resolved = Assert.Single(scenario.Resolved);
        Assert.Equal(scenario.State.CustomerId, Assert.Single(scenario.ResolvedCustomers));
        Assert.Equal("APPROVED", resolved.ApprovalResult);
        Assert.Equal("PENDING", resolved.Status);
        Assert.Equal(scenario.State.BranchId, resolved.BranchId);
        Assert.Equal("BK-20261118-000123", resolved.BookingCode);
        Assert.Contains("thanh toán tiền cọc", resolved.Message);
        Assert.Equal(DateTimeKind.Utc, resolved.Timestamp.Kind);
        var queue = Assert.Single(scenario.QueueChanges);
        Assert.Equal(SignalREvents.QueueChangeRemoved, queue.Change);
        Assert.Equal(SignalREvents.QueueReasonApproved, queue.Reason);
        Assert.Empty(scenario.BookingEvents);
        var email = Assert.Single(scenario.Builder.Email.Sent);
        Assert.Equal("khach@test.local", email.To);
        Assert.Contains("09:45 18/11/2026", email.Body); // hạn trả cọc = 09:30 + 15'
        Assert.DoesNotContain("vnpay", email.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approve_EmailOrSignalRFailure_DoesNotChangeTheResult()
    {
        var scenario = new Scenario();
        scenario.Builder.Email.FailOnSend = true;
        scenario.Builder.Broadcast.Setup(x => x.BroadcastBookingApprovalResolvedAsync(It.IsAny<BookingApprovalResolvedNotificationDto>(), It.IsAny<Guid?>()))
            .ThrowsAsync(new InvalidOperationException("hub hỏng"));

        var result = await scenario.ApproveAsync(0.3m);

        Assert.Equal("PENDING", result.Status);
        Assert.Contains(scenario.Builder.NotificationLogger.Entries, e => e.Level == LogLevel.Error && e.Exception != null);
    }

    // ───────────────────────── Duyệt 0 % (Q4, D3, D8) ─────────────────────────

    [Theory]
    [InlineData("0", "0")]
    [InlineData("0.02", "4999")] // hai bên lệch công thức: vẫn thu tại quầy, log Error
    [Trait("Catalog", "BE-IT-07")]
    public async Task Approve_TadssAmountBelowMinimum_ConfirmsPostpaidWithCancelToken(string rate, string amount)
    {
        var scenario = new Scenario();
        scenario.State.Invoice!.FinalTotal = 249_950m; // 0.02 ⇒ 4.999 đ bên BE, nên bước 4b phải cho qua bằng tỷ lệ 0
        scenario.OverrideResult = Success(ApproveResponse(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

        var result = await scenario.ApproveAsync(0m);

        var expectedToken = CancelTokenHelper.ComputeExpiry(PlayStartUtc, Now);
        scenario.Builder.BookingRepository.Verify(x => x.ApproveWithoutDepositAsync(
            scenario.State.Id, scenario.ActorId, Reason, It.Is<string>(h => h.Length > 0), expectedToken, Now), Times.Once);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal(0m, result.DepositRate);
        Assert.Equal(0m, result.DepositAmount);
        Assert.Equal(249_950m, result.RemainingAmount);
        Assert.Null(result.ExpiresAt);
        Assert.Equal(amount != "0", scenario.Builder.Logger.Entries.Any(e => e.Level == LogLevel.Error));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-07")]
    public async Task Approve_Zero_EmailsAConfirmationWhoseCancelLinkMatchesTheStoredToken()
    {
        var scenario = new Scenario { OverrideResult = Success(ApproveResponse(0m, 0m)) };

        await scenario.ApproveAsync(0m);

        var email = Assert.Single(scenario.Builder.Email.Sent);
        var token = Uri.UnescapeDataString(email.Body.Split("token=")[1].Split('"')[0]);
        Assert.Equal(scenario.State.CancelTokenHash, CancelTokenHelper.Hash(token));
        Assert.Contains("thanh toán tại quầy", Assert.Single(scenario.Resolved).Message);
        Assert.Equal("CONFIRMED", scenario.Resolved[0].Status);
    }

    [Fact]
    public async Task Approve_ZeroWithPromotion_CountsTheUsageOnce()
    {
        var scenario = new Scenario { OverrideResult = Success(ApproveResponse(0m, 0m)) };
        var promotionId = Guid.NewGuid();
        scenario.State.BookingPromotion = new BookingPromotion { PromotionId = promotionId };

        await scenario.ApproveAsync(0m);

        scenario.Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(promotionId), Times.Once);
    }

    [Fact]
    public async Task Approve_ZeroWithoutPromotion_DoesNotCountUsage()
    {
        var scenario = new Scenario { OverrideResult = Success(ApproveResponse(0m, 0m)) };

        await scenario.ApproveAsync(0m);

        scenario.Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
    }

    // ───────────────────────── TADSS trả 200 nhưng không áp được ─────────────────────────

    [Theory]
    [InlineData("200001", "VND")] // lớn hơn tổng đơn
    [InlineData("60000", "USD")]
    public async Task Approve_DepositDoesNotFitTheBooking_Is503WithoutWriting(string amount, string currency)
    {
        var scenario = new Scenario
        {
            OverrideResult = Success(ApproveResponse(0.3m, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency))
        };

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync(0.3m))).ShouldBeAppException(503, ErrorCodes.TadssUnavailable);
        scenario.VerifyNoWrite();
        scenario.VerifyNothingAnnounced();
        Assert.Contains(scenario.Builder.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(DecisionId.ToString()));
    }

    [Fact]
    [Trait("Catalog", "BE-IT-15")]
    public async Task Approve_PlayStartedBeforeBeWrites_Is409ExpiredWithoutWriting()
    {
        // Lệch đồng hồ: hạn duyệt còn nhưng giờ chơi đã tới khi TADSS trả lời
        var scenario = new Scenario();
        scenario.State.BookingCourts.First().Date = DateOnly.FromDateTime(DateTimeHelper.ToVietnamTime(Now));
        scenario.State.BookingCourts.First().StartTime = TimeOnly.FromDateTime(DateTimeHelper.ToVietnamTime(Now.AddSeconds(30)));
        scenario.TadssDelay = TimeSpan.FromMinutes(1);

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync(0.3m))).ShouldBeAppException(409, ErrorCodes.ApprovalExpired);
        scenario.VerifyNoWrite();
        Assert.Contains(scenario.Builder.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(DecisionId.ToString()));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("approve-zero")]
    [InlineData("reject")]
    [Trait("Catalog", "BE-IT-15")]
    public async Task GateLost_Is409FromTheReloadedBooking_AndAnnouncesNothing(string action)
    {
        var scenario = new Scenario { GateWins = false };
        scenario.OverrideResult = action switch
        {
            "approve-zero" => Success(ApproveResponse(0m, 0m)),
            "reject" => Success(RejectResponse()),
            _ => Success(ApproveResponse(0.3m, 60_000m))
        };

        var error = await Assert.ThrowsAsync<AppException>(() => action == "reject" ? scenario.RejectAsync() : scenario.ApproveAsync(
            action == "approve-zero" ? 0m : 0.3m));

        error.ShouldBeAppException(409, ErrorCodes.ApprovalAlreadyResolved);
        scenario.VerifyNothingAnnounced();
        Assert.Contains(scenario.Builder.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(DecisionId.ToString()));
    }

    // ───────────────────────── Lỗi TADSS (BE-UT-04 qua service) ─────────────────────────

    public static TheoryData<int?, string?, TadssFailureKind, int, string> Failures => new()
    {
        { null, null, TadssFailureKind.Timeout, 503, ErrorCodes.TadssUnavailable },
        { null, null, TadssFailureKind.Network, 503, ErrorCodes.TadssUnavailable },
        { 503, "PERSISTENCE_UNAVAILABLE", TadssFailureKind.Http, 503, ErrorCodes.TadssUnavailable },
        { 401, "UNAUTHORIZED", TadssFailureKind.Http, 503, ErrorCodes.TadssUnavailable },
        { 404, null, TadssFailureKind.Http, 503, ErrorCodes.TadssUnavailable },
        { 200, null, TadssFailureKind.InvalidResponse, 503, ErrorCodes.TadssUnavailable },
        { 404, "DECISION_NOT_FOUND", TadssFailureKind.Http, 409, ErrorCodes.ApprovalDecisionNotFound },
        { 409, "NOT_PENDING_APPROVAL", TadssFailureKind.Http, 409, ErrorCodes.BookingNotPendingApproval },
        { 409, "APPROVAL_EXPIRED", TadssFailureKind.Http, 409, ErrorCodes.ApprovalExpired },
        { 400, "MISSING_HEADER", TadssFailureKind.Http, 500, ErrorCodes.InternalError }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssFailure_MapsToTheApiCode_AndLeavesTheBookingWaiting(
        int? status, string? code, TadssFailureKind kind, int expectedStatus, string expectedCode)
    {
        var scenario = new Scenario { OverrideResult = Failure(status, code, kind) };

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync())).ShouldBeAppException(expectedStatus, expectedCode);
        scenario.VerifyNoWrite();
        scenario.VerifyNothingAnnounced();
        scenario.Builder.Tadss.Verify(x => x.GetDecisionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(BookingStatus.PENDING_APPROVAL, scenario.State.Status);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-14")]
    public async Task TadssUnauthorized_IsLoggedAsError()
    {
        var scenario = new Scenario { OverrideResult = Failure(401, "UNAUTHORIZED") };

        await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync());

        Assert.Contains(scenario.Builder.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("corr-1"));
    }

    [Fact]
    public async Task TadssRejectsDepositRate_Is400OnDepositRate()
    {
        var scenario = new Scenario
        {
            OverrideResult = TadssCallResult<TadssOverrideResponseDto>.Failure(
                TadssFailureKind.Http, "corr-1", 400, "VALIDATION_ERROR", errorFields: ["deposit.rate"])
        };

        var error = await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync());

        error.ShouldBeAppException(400, ErrorCodes.ValidationError);
        Assert.True(error.Errors!.ContainsKey("DepositRate"));
    }

    // ───────────────────────── Từ chối (Task 11) ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-06")]
    public async Task Reject_SendsRejectWithoutDeposit_CancelsAndReleasesTheSlot()
    {
        var scenario = new Scenario { OverrideResult = Success(RejectResponse()) };

        var result = await scenario.RejectAsync();

        var request = Assert.Single(scenario.OverrideRequests);
        Assert.Equal(TadssOverrideAction.REJECT, request.Action);
        Assert.Null(request.Deposit);
        Assert.Equal(Reason, request.Reason);
        scenario.Builder.BookingRepository.Verify(x => x.RejectByManagerAsync(scenario.State.Id, scenario.ActorId, Reason, Now), Times.Once);
        Assert.Equal("REJECTED", result.ApprovalResult);
        Assert.Equal("CANCELLED", result.Status);
        Assert.Null(result.DepositRate);
        Assert.Null(result.DepositAmount);
        Assert.Null(result.RemainingAmount);
        Assert.Null(result.DepositSource);
        Assert.Null(result.ExpiresAt);
        Assert.Equal(scenario.ActorId, result.ResolvedBy);
        scenario.Builder.SlotInterestNotifier.Verify(x => x.NotifyReleasedSlotsAsync(It.Is<Booking>(bk => bk.Id == scenario.State.Id)), Times.Once);
        scenario.Builder.PromotionRepository.Verify(x => x.DecrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
        scenario.Builder.PromotionRepository.Verify(x => x.IncrementUsageCountAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-18")]
    public async Task Reject_AnnouncesToCustomerStaffAndTimeGrid_AndEmailsWithoutTheManagerReason()
    {
        var scenario = new Scenario { OverrideResult = Success(RejectResponse()) };

        await scenario.RejectAsync();

        var resolved = Assert.Single(scenario.Resolved);
        Assert.Equal("REJECTED", resolved.ApprovalResult);
        Assert.Equal("CANCELLED", resolved.Status);
        Assert.Equal(SignalREvents.QueueReasonRejected, Assert.Single(scenario.QueueChanges).Reason);
        var cancelled = Assert.Single(scenario.BookingEvents);
        Assert.Equal(SignalREvents.BookingCancelled, cancelled.Event);
        Assert.True(cancelled.TimeGrid);
        var email = Assert.Single(scenario.Builder.Email.Sent);
        Assert.DoesNotContain(Reason, email.Body);
        Assert.Contains("0281234567", email.Body);
    }

    // ───────────────────────── Đồng bộ Q9 (Task 12) ─────────────────────────

    private static TadssCallResult<TadssDecisionDetailDto> Detail(Guid reservationId, TadssOverrideResponseDto? stored) =>
        TadssCallResult<TadssDecisionDetailDto>.Success(new TadssDecisionDetailDto
        {
            DecisionId = DecisionId,
            ReservationId = reservationId,
            Request = TadssTestData.DecisionRequest(full: true),
            Result = new TadssDecisionDetailResultDto
            {
                Status = TadssDecisionStatus.NEED_APPROVAL, StrategyStatus = TadssDecisionStatus.NEED_APPROVAL, ReasonCode = "X",
                Reasons = [], Recommendations = [], ApprovalExpiresAt = Now.AddMinutes(20)
            },
            Execution = new TadssDecisionExecutionDto
            {
                StrategyName = "Default", StrategyVersion = "2.1.0", ExecutionType = "PRIMARY", ExecutionTimeMs = 5, FallbackUsed = false, CacheHit = false
            },
            Override = stored,
            CreatedAt = Now.AddMinutes(-10)
        }, 200, "corr-get");

    [Theory]
    [InlineData("ALREADY_OVERRIDDEN")]
    [InlineData("IDEMPOTENCY_KEY_REUSE")]
    [Trait("Catalog", "BE-IT-13")]
    public async Task Sync_AppliesTheStoredOverrideOfAnotherManager_ButStillAnswers409(string code)
    {
        var scenario = new Scenario { OverrideResult = Failure(409, code) };
        var otherManager = Guid.NewGuid();
        scenario.Builder.UserRepository.Setup(x => x.GetUserByIdAsync(otherManager)).ReturnsAsync(new User { Id = otherManager });
        var stored = ApproveResponse(0.3m, 60_000m, actor: otherManager.ToString()) ;
        stored.Reason = "Lý do của Quản lý B";
        scenario.DetailResult = Detail(scenario.State.Id, stored);

        var error = await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync(0.4m)); // A bấm 40 % nhưng B đã duyệt 30 %

        error.ShouldBeAppException(409, ErrorCodes.ApprovalAlreadyResolved);
        scenario.Builder.BookingRepository.Verify(x => x.ApproveWithDepositAsync(
            scenario.State.Id, otherManager, "Lý do của Quản lý B", 0.3m, 60_000m, It.IsAny<PaymentHoldWindow>(), Now), Times.Once);
        Assert.Single(scenario.Resolved);   // khách chưa được báo ở lần gọi trước
        Assert.Single(scenario.Builder.Email.Sent);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-13")]
    public async Task Sync_StoredRejectWhileUserClickedApprove_AppliesTheReject()
    {
        var scenario = new Scenario { OverrideResult = Failure(409, "ALREADY_OVERRIDDEN") };
        scenario.DetailResult = Detail(scenario.State.Id, RejectResponse(actor: scenario.ActorId.ToString(), reason: "Sân bảo trì"));
        scenario.Builder.UserRepository.Setup(x => x.GetUserByIdAsync(scenario.ActorId)).ReturnsAsync(new User { Id = scenario.ActorId });

        await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync());

        scenario.Builder.BookingRepository.Verify(x => x.RejectByManagerAsync(scenario.State.Id, scenario.ActorId, "Sân bảo trì", Now), Times.Once);
        scenario.Builder.SlotInterestNotifier.Verify(x => x.NotifyReleasedSlotsAsync(It.IsAny<Booking>()), Times.Once);
    }

    [Theory]
    [InlineData("admin")]              // gọi thẳng TADSS bằng Swagger
    [InlineData("unknown-guid")]       // GUID của user ở database khác
    public async Task Sync_ActorThatIsNotAUserOfThisBe_AppliesWithEmptyResolver(string actor)
    {
        var scenario = new Scenario { OverrideResult = Failure(409, "ALREADY_OVERRIDDEN") };
        var actorId = actor == "unknown-guid" ? Guid.NewGuid().ToString() : actor;
        scenario.DetailResult = Detail(scenario.State.Id, ApproveResponse(0.3m, 60_000m, actor: actorId));

        await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync());

        scenario.Builder.BookingRepository.Verify(x => x.ApproveWithDepositAsync(
            scenario.State.Id, null, It.IsAny<string>(), 0.3m, 60_000m, It.IsAny<PaymentHoldWindow>(), Now), Times.Once);
        Assert.Contains(scenario.Builder.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(actorId));
    }

    public static TheoryData<string> NoSyncCases => new() { "get-fails", "no-override", "other-reservation", "other-decision", "gate-lost" };

    [Theory]
    [MemberData(nameof(NoSyncCases))]
    [Trait("Catalog", "BE-IT-13")]
    public async Task Sync_WhenItCannotApply_JustAnswers409(string @case)
    {
        var scenario = new Scenario { OverrideResult = Failure(409, "IDEMPOTENCY_KEY_REUSE") };
        var stored = ApproveResponse(0.3m, 60_000m);
        scenario.DetailResult = @case switch
        {
            "get-fails" => null,
            "no-override" => Detail(scenario.State.Id, null),
            "other-reservation" => Detail(Guid.NewGuid(), stored),
            "other-decision" => Detail(scenario.State.Id, ApproveResponseFor(Guid.NewGuid())),
            _ => Detail(scenario.State.Id, stored)
        };
        scenario.GateWins = @case != "gate-lost";

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync())).ShouldBeAppException(409, ErrorCodes.ApprovalAlreadyResolved);
        if (@case != "gate-lost")
            scenario.VerifyNoWrite();
        scenario.VerifyNothingAnnounced();
    }

    private static TadssOverrideResponseDto ApproveResponseFor(Guid decisionId)
    {
        var response = ApproveResponse(0.3m, 60_000m);
        response.DecisionId = decisionId;
        return response;
    }

    [Fact]
    public async Task Sync_StoredDepositDoesNotFit_JustAnswers409WithoutWriting()
    {
        var scenario = new Scenario { OverrideResult = Failure(409, "ALREADY_OVERRIDDEN") };
        scenario.DetailResult = Detail(scenario.State.Id, ApproveResponse(1m, 999_999m));

        (await Assert.ThrowsAsync<AppException>(() => scenario.ApproveAsync())).ShouldBeAppException(409, ErrorCodes.ApprovalAlreadyResolved);
        scenario.VerifyNoWrite();
    }
}
