using System.Globalization;
using Microsoft.Extensions.Options;
using SmashCourt_BE.Common;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.AccessControl;
using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>
/// Hàng chờ, duyệt và từ chối mức cọc. Thứ tự: kiểm quyền và trạng thái theo DB của BE ⇒ ghi đè ở TADSS (ngoài transaction)
/// ⇒ ghi kết quả bằng câu cập nhật có điều kiện ⇒ thông báo. Lỗi TADSS không bao giờ thành cọc 100 %,
/// và không bao giờ hồi sinh đơn đã hết hạn hay đã hủy.
/// </summary>
public class BookingApprovalService : IBookingApprovalService
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IBookingRepository _bookingRepo;
    private readonly IUserBranchRepository _userBranchRepo;
    private readonly IUserRepository _userRepo;
    private readonly IBranchScopeResolver _branchScope;
    private readonly ITadssClient _tadss;
    private readonly PromotionEngineService _promotionEngine;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBookingNotificationService _notifications;
    private readonly IOptionsMonitor<TadssSettings> _tadssSettings;
    private readonly IOptionsMonitor<BookingPaymentSettings> _paymentSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BookingApprovalService> _logger;

    public BookingApprovalService(
        IBookingRepository bookingRepo,
        IUserBranchRepository userBranchRepo,
        IUserRepository userRepo,
        IBranchScopeResolver branchScope,
        ITadssClient tadss,
        PromotionEngineService promotionEngine,
        IUnitOfWork unitOfWork,
        IBookingNotificationService notifications,
        IOptionsMonitor<TadssSettings> tadssSettings,
        IOptionsMonitor<BookingPaymentSettings> paymentSettings,
        TimeProvider timeProvider,
        ILogger<BookingApprovalService> logger)
    {
        _bookingRepo = bookingRepo;
        _userBranchRepo = userBranchRepo;
        _userRepo = userRepo;
        _branchScope = branchScope;
        _tadss = tadss;
        _promotionEngine = promotionEngine;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _tadssSettings = tadssSettings;
        _paymentSettings = paymentSettings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Hàng chờ
    // ────────────────────────────────────────────────────────────────────────────

    // Lấy hàng chờ duyệt từ DB của BE; Manager chỉ thấy chi nhánh được gán, OWNER thấy mọi chi nhánh hoặc lọc một
    public async Task<PagedResult<PendingApprovalBookingDto>> GetPendingApprovalsAsync(
        PendingApprovalListQuery query, Guid userId, string role)
    {
        var branchId = await _branchScope.ResolveOptionalBranchIdAsync(query.BranchId, userId, Enum.Parse<UserRole>(role));
        var page = await _bookingRepo.GetPendingApprovalsAsync(branchId, UtcNow(), query.Page, query.PageSize);

        return new PagedResult<PendingApprovalBookingDto>
        {
            Items = page.Items.Select(MapPending).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalItems = page.TotalItems
        };
    }

    private static PendingApprovalBookingDto MapPending(Booking b)
    {
        var firstCourt = b.BookingCourts.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).FirstOrDefault();
        return new PendingApprovalBookingDto
        {
            BookingId = b.Id,
            BookingCode = b.BookingCode,
            BranchId = b.BranchId,
            BranchName = b.Branch?.Name ?? string.Empty,
            CustomerId = b.CustomerId,
            CustomerName = b.Customer?.FullName ?? b.GuestName ?? string.Empty,
            CustomerPhone = b.Customer?.Phone ?? b.GuestPhone,
            IsGuest = b.CustomerId == null,
            LoyaltyTierName = b.Customer?.CustomerLoyalty?.Tier?.Name,
            BookingDate = b.BookingDate,
            StartTime = firstCourt?.StartTime.ToTimeSpan() ?? TimeSpan.Zero,
            EndTime = firstCourt?.EndTime.ToTimeSpan() ?? TimeSpan.Zero,
            Courts = b.BookingCourts
                .Select(bc => new PendingApprovalCourtDto { CourtId = bc.CourtId, CourtName = bc.Court?.Name ?? string.Empty })
                .ToList(),
            FinalTotal = b.Invoice?.FinalTotal ?? 0m,
            ApprovalReasonCode = b.ApprovalReasonCode,
            ApprovalReasons = b.ApprovalReasons ?? [],
            ApprovalExpiresAt = b.ApprovalExpiresAt ?? default,
            CreatedAt = b.CreatedAt,
            Note = b.Note
        };
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Duyệt / từ chối
    // ────────────────────────────────────────────────────────────────────────────

    // Duyệt đơn chờ duyệt kèm tỷ lệ cọc
    public async Task<BookingApprovalResultDto> ApproveAsync(Guid bookingId, ApproveBookingDto dto, Guid actorId, string role)
    {
        var booking = await LoadApprovableAsync(bookingId, actorId, role);
        var rate = dto.DepositRate ?? throw new ArgumentException("DepositRate đã được kiểm ở model validation.", nameof(dto));
        EnsureDepositReachesMinimum(booking, rate);

        var request = new TadssOverrideRequestDto
        {
            Action = TadssOverrideAction.APPROVE,
            Deposit = new TadssOverrideDepositDto { Rate = rate },
            Reason = dto.Reason!
        };
        return await DecideAsync(booking, request, actorId);
    }

    // Từ chối đơn chờ duyệt: hủy đơn, nhả sân
    public async Task<BookingApprovalResultDto> RejectAsync(Guid bookingId, RejectBookingDto dto, Guid actorId, string role)
    {
        var booking = await LoadApprovableAsync(bookingId, actorId, role);

        // Deposit = null không được ghi ra JSON: schema của TADSS cấm khóa deposit khi REJECT
        var request = new TadssOverrideRequestDto { Action = TadssOverrideAction.REJECT, Reason = dto.Reason! };
        return await DecideAsync(booking, request, actorId);
    }

    // Đơn tồn tại ⇒ đúng chi nhánh ⇒ còn duyệt được. Dừng ở bước đầu không đạt, chưa gọi TADSS
    private async Task<Booking> LoadApprovableAsync(Guid bookingId, Guid actorId, string role)
    {
        var booking = await _bookingRepo.GetByIdWithDetailsAsync(bookingId)
            ?? throw new AppException(404, "Không tìm thấy booking", ErrorCodes.NotFound);

        if (role != UserRole.OWNER.ToString() && !await _userBranchRepo.IsUserInBranchAsync(actorId, booking.BranchId))
            throw new AppException(403, "Bạn không có quyền xử lý đơn của chi nhánh này", ErrorCodes.Forbidden);

        if (ApprovalConflictResolver.Resolve(booking, UtcNow()) is { } conflict)
            throw conflict;

        return booking;
    }

    // Cùng công thức quy đổi của TADSS: số tiền lớn hơn 0 nhưng dưới mức thu online tối thiểu thì Quản lý phải chọn lại
    private void EnsureDepositReachesMinimum(Booking booking, decimal rate)
    {
        var amount = Math.Round(FinalTotal(booking) * rate, 0, MidpointRounding.AwayFromZero);
        var minimum = _paymentSettings.CurrentValue.MinOnlinePaymentAmount;
        if (amount > 0m && amount < minimum)
            throw new AppException(400, "Dữ liệu đầu vào không hợp lệ", ErrorCodes.ValidationError,
                new Dictionary<string, string[]>
                {
                    ["DepositRate"] = [$"Số tiền cọc tối thiểu {minimum.ToString("N0", Vietnamese)} đ, hoặc chọn 0 %"]
                });
    }

    private async Task<BookingApprovalResultDto> DecideAsync(Booking booking, TadssOverrideRequestDto request, Guid actorId)
    {
        var decisionId = booking.TadssDecisionId
            ?? throw new InvalidOperationException($"Booking {booking.Id} đã qua bước kiểm nhưng không có tadss_decision_id.");

        // Không hủy theo request: TADSS đã ghi đè thì BE vẫn phải ghi theo dù Quản lý đóng tab
        var result = await _tadss.OverrideAsync(decisionId, request, actorId.ToString(), CancellationToken.None);
        if (!result.IsSuccess)
        {
            var failure = OverrideFailureResolver.Resolve(result);
            _logger.Log(failure.LogLevel, "{Failure}. BookingId={BookingId}, DecisionId={DecisionId}",
                failure.LogMessage, booking.Id, decisionId);
            if (failure.SyncStoredOverride)
                await SyncStoredOverrideAsync(booking, decisionId);
            throw failure.Error;
        }

        var applied = await ApplyOverrideAsync(booking, result.Value, actorId)
            ?? throw await LostGateAsync(booking.Id, decisionId);
        return ToResult(applied);
    }

    // Ghi kết quả ghi đè vào đơn (dùng cả khi đồng bộ). Trả đơn đọc lại sau commit, hoặc null khi thua câu cập nhật có điều kiện
    private async Task<Booking?> ApplyOverrideAsync(Booking booking, TadssOverrideResponseDto decision, Guid? resolvedBy)
    {
        // Giờ duyệt là lúc BE ghi, sau khi TADSS trả lời
        var decidedAt = UtcNow();
        string? rawCancelToken = null;
        bool won;

        if (decision.Action == TadssOverrideAction.APPROVE)
        {
            var deposit = RequireUsableDeposit(booking, decision);
            var playStartUtc = PlayStartUtc(booking);
            // Giờ chơi đã tới (lệch đồng hồ) thì không còn thời gian trả cọc, cũng không xác nhận đơn đã tới giờ
            if (!PaymentHoldPolicy.TryForApproval(
                    decidedAt, playStartUtc, _tadssSettings.CurrentValue.ApprovedPaymentWindowMinutes, out var window))
            {
                _logger.LogError(
                    "TADSS đã duyệt booking {BookingId} (decision {DecisionId}) nhưng giờ chơi đã tới lúc BE ghi; không ghi, cần đối soát",
                    booking.Id, decision.DecisionId);
                throw ApprovalConflictResolver.Conflict(ErrorCodes.ApprovalExpired);
            }

            // Rẽ nhánh theo số tiền TADSS trả: dưới mức tối thiểu thì không thu online được, xác nhận và thu tại quầy
            if (deposit.Amount >= _paymentSettings.CurrentValue.MinOnlinePaymentAmount)
            {
                using var transaction = TransactionScopeHelper.ReadCommitted();
                won = await _bookingRepo.ApproveWithDepositAsync(
                    booking.Id, resolvedBy, decision.Reason, deposit.Rate, deposit.Amount, window, decidedAt);
                if (won)
                    transaction.Complete();
            }
            else
            {
                if (deposit.Amount > 0m)
                    _logger.LogError(
                        "TADSS quy đổi cọc {Amount} đ cho booking {BookingId} (decision {DecisionId}), dưới mức tối thiểu dù BE đã kiểm trước: " +
                        "hai bên lệch công thức; đơn được xác nhận và thu toàn bộ tại quầy",
                        deposit.Amount, booking.Id, decision.DecisionId);

                // Đơn xác nhận không qua VNPay: token hủy ghi cùng câu cập nhật, lượt khuyến mãi tính ngay
                var cancelToken = CancelTokenHelper.Generate(playStartUtc, decidedAt);
                using var transaction = TransactionScopeHelper.ReadCommitted();
                won = await _bookingRepo.ApproveWithoutDepositAsync(
                    booking.Id, resolvedBy, decision.Reason, cancelToken.TokenHash, cancelToken.ExpiresAtUtc, decidedAt);
                if (won)
                {
                    if (booking.BookingPromotion is { } promotion)
                        await _promotionEngine.IncrementUsageCountAsync(promotion.PromotionId);
                    transaction.Complete();
                    rawCancelToken = cancelToken.RawToken;
                }
            }
        }
        else
        {
            using var transaction = TransactionScopeHelper.ReadCommitted();
            won = await _bookingRepo.RejectByManagerAsync(booking.Id, resolvedBy, decision.Reason, decidedAt);
            if (won)
                transaction.Complete();
        }

        if (!won)
            return null;

        // ExecuteUpdate bỏ qua change tracker: đọc lại để response và thông báo thấy trạng thái mới
        _unitOfWork.ClearChangeTracker();
        var updated = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id)
            ?? throw new InvalidOperationException($"Không đọc lại được booking {booking.Id} sau khi ghi kết quả duyệt.");
        await _notifications.NotifyApprovalResolvedAsync(updated, decision.Action == TadssOverrideAction.REJECT, rawCancelToken);
        return updated;
    }

    // Mức cọc vượt tổng đơn hoặc sai tiền tệ thì không áp được: xử lý như TADSS lỗi, đơn vẫn chờ duyệt
    private TadssDepositDto RequireUsableDeposit(Booking booking, TadssOverrideResponseDto decision)
    {
        var finalTotal = FinalTotal(booking);
        if (decision.Deposit is { Rate: >= 0m and <= 1m, Amount: >= 0m, Currency: "VND" } deposit && deposit.Amount <= finalTotal)
            return deposit;

        _logger.LogError(
            "Mức cọc TADSS trả cho booking {BookingId} (decision {DecisionId}) không áp được: rate={Rate}, amount={Amount}, currency={Currency}, " +
            "finalTotal={FinalTotal}; đơn vẫn chờ duyệt",
            booking.Id, decision.DecisionId, decision.Deposit?.Rate, decision.Deposit?.Amount, decision.Deposit?.Currency, finalTotal);
        throw new AppException(503, OverrideFailureResolver.UnavailableMessage, ErrorCodes.TadssUnavailable);
    }

    // TADSS đã ghi đè nhưng đơn đổi trước khi BE ghi: không hồi sinh, báo 409 theo trạng thái đọc lại
    private async Task<AppException> LostGateAsync(Guid bookingId, Guid decisionId)
    {
        _unitOfWork.ClearChangeTracker();
        var current = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);
        _logger.LogWarning(
            "TADSS đã ghi đè quyết định {DecisionId} nhưng booking {BookingId} đã đổi trước khi BE ghi (trạng thái {Status}); " +
            "không hồi sinh, cần đối soát",
            decisionId, bookingId, current?.Status);
        return (current == null ? null : ApprovalConflictResolver.Resolve(current, UtcNow()))
               ?? ApprovalConflictResolver.Conflict(ErrorCodes.ApprovalAlreadyResolved);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Đồng bộ ghi đè đã lưu
    // ────────────────────────────────────────────────────────────────────────────

    // TADSS đã có ghi đè mà đơn ở BE vẫn chờ: áp đúng ghi đè đã lưu. Caller vẫn trả 409 vì người đang bấm không phải người quyết định
    private async Task SyncStoredOverrideAsync(Booking booking, Guid decisionId)
    {
        try
        {
            var detail = await _tadss.GetDecisionAsync(decisionId, CancellationToken.None);
            if (!detail.IsSuccess)
            {
                _logger.LogWarning("Không đọc được chi tiết quyết định {DecisionId} để đồng bộ booking {BookingId} ({Kind})",
                    decisionId, booking.Id, detail.FailureKind);
                return;
            }

            if (detail.Value.ReservationId != booking.Id || detail.Value.Override is not { } stored || stored.DecisionId != decisionId)
            {
                _logger.LogWarning(
                    "Chi tiết quyết định {DecisionId} không có ghi đè dùng được cho booking {BookingId} (reservationId {ReservationId}); không đồng bộ",
                    decisionId, booking.Id, detail.Value.ReservationId);
                return;
            }

            var resolvedBy = await ResolveActorAsync(stored.ActorId, booking.Id);
            var applied = await ApplyOverrideAsync(booking, stored, resolvedBy);
            if (applied == null)
                _logger.LogWarning("Đồng bộ ghi đè {DecisionId} thua câu cập nhật: booking {BookingId} không còn chờ duyệt", decisionId, booking.Id);
            else
                _logger.LogInformation("Đã đồng bộ ghi đè {Action} của quyết định {DecisionId} vào booking {BookingId}",
                    stored.Action, decisionId, booking.Id);
        }
        catch (AppException ex)
        {
            _logger.LogWarning("Không đồng bộ được ghi đè {DecisionId} vào booking {BookingId}: {Code} {Message}",
                decisionId, booking.Id, ex.ErrorCode, ex.Message);
        }
    }

    // Người duyệt phải là user có thật của BE (khóa ngoại); không phải thì để trống, ai duyệt tra ở audit của TADSS
    private async Task<Guid?> ResolveActorAsync(string actorId, Guid bookingId)
    {
        if (Guid.TryParse(actorId, out var userId) && await _userRepo.GetUserByIdAsync(userId) != null)
            return userId;

        _logger.LogWarning(
            "Người duyệt {ActorId} trên TADSS không phải user của BE; đồng bộ booking {BookingId} với người duyệt trống",
            actorId, bookingId);
        return null;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Phụ trợ
    // ────────────────────────────────────────────────────────────────────────────

    private static BookingApprovalResultDto ToResult(Booking booking)
    {
        var deposit = DepositFieldsMapper.FromInvoice(booking.Status, booking.Invoice);
        return new BookingApprovalResultDto
        {
            BookingId = booking.Id,
            BookingCode = booking.BookingCode,
            ApprovalResult = booking.ApprovalResult?.ToString() ?? string.Empty,
            Status = booking.Status.ToString(),
            FinalTotal = booking.Invoice?.FinalTotal ?? 0m,
            DepositRate = deposit?.Rate,
            DepositAmount = deposit?.Amount,
            RemainingAmount = deposit?.RemainingAmount,
            DepositSource = deposit?.Source.ToString(),
            ExpiresAt = booking.Status == BookingStatus.PENDING ? booking.Invoice?.ExpiresAt : null,
            ResolvedBy = booking.ApprovalResolvedBy,
            ResolvedAt = booking.ApprovalResolvedAt
        };
    }

    private static decimal FinalTotal(Booking booking) =>
        booking.Invoice?.FinalTotal ?? throw new InvalidOperationException($"Booking {booking.Id} chờ duyệt nhưng không có hóa đơn.");

    // Đơn online một ngày, một giờ chung: giờ bắt đầu sớm nhất đổi sang UTC
    private static DateTime PlayStartUtc(Booking booking)
    {
        var first = booking.BookingCourts.OrderBy(bc => bc.Date).ThenBy(bc => bc.StartTime).FirstOrDefault()
            ?? throw new InvalidOperationException($"Booking {booking.Id} không có sân.");
        return DateTimeHelper.ToUtcFromVietnam(first.Date, first.StartTime);
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
