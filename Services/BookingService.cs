using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Services.Deposit;
using SmashCourt_BE.Common;
using SmashCourt_BE.Common.Constants;
using SmashCourt_BE.Configurations;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.DTOs.SignalR;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.ViewModels;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;


namespace SmashCourt_BE.Services
{
    public class BookingService : IBookingService
    {
        private readonly IBookingRepository _bookingRepo;
        private readonly ISlotLockRepository _slotLockRepo;
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly IPaymentRepository _paymentRepo;
        private readonly IRefundRepository _refundRepo;
        private readonly IBranchPriceService _priceService;
        private readonly IPromotionRepository _promotionRepo;
        private readonly PromotionEngineService _promotionEngine;
        private readonly ICustomerLoyaltyRepository _loyaltyRepo;
        private readonly ILoyaltyTierRepository _loyaltyTierRepo;
        private readonly ILoyaltyTransactionRepository _loyaltyTransactionRepo;
        private readonly IBranchServiceRepository _branchServiceRepo;
        private readonly ICourtRepository _courtRepo;
        private readonly IUserBranchRepository _userBranchRepo;
        private readonly IUserRepository _userRepo;
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly IOnlinePaymentStarter _paymentStarter;
        private readonly EmailService _emailService;
        private readonly ICodeGeneratorService _codeGeneratorService;
        private readonly ISlotInterestRepository _slotInterestRepo;
        private readonly IBookingNotificationService _notifications;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<BookingService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IBroadcastService _broadcast;
        private readonly IBookingDepositService _depositService;
        private readonly IOptionsMonitor<BookingPaymentSettings> _paymentSettings;
        // "Bây giờ" của luồng đặt online; các phần cũ vẫn dùng DateTime.UtcNow
        private readonly TimeProvider _timeProvider;

        public BookingService(
            IBookingRepository bookingRepo,
            ISlotLockRepository slotLockRepo,
            IInvoiceRepository invoiceRepo,
            IPaymentRepository paymentRepo,
            IRefundRepository refundRepo,
            IBranchPriceService priceService,
            IPromotionRepository promotionRepo,
            PromotionEngineService promotionEngine,
            ICustomerLoyaltyRepository loyaltyRepo,
            ILoyaltyTierRepository loyaltyTierRepo,
            ILoyaltyTransactionRepository loyaltyTransactionRepo,
            IBranchServiceRepository branchServiceRepo,
            ICourtRepository courtRepo,
            IUserBranchRepository userBranchRepo,
            IUserRepository userRepo,
            ITimeSlotRepository timeSlotRepo,
            IOnlinePaymentStarter paymentStarter,
            EmailService emailService,
            ICodeGeneratorService codeGeneratorService,
            ISlotInterestRepository slotInterestRepo,
            IBookingNotificationService notifications,
            IUnitOfWork unitOfWork,
            ILogger<BookingService> logger,
            IConfiguration configuration,
            IBroadcastService broadcast,
            IBookingDepositService depositService,
            IOptionsMonitor<BookingPaymentSettings> paymentSettings,
            TimeProvider timeProvider)
        {
            _bookingRepo = bookingRepo;
            _slotLockRepo = slotLockRepo;
            _invoiceRepo = invoiceRepo;
            _paymentRepo = paymentRepo;
            _refundRepo = refundRepo;
            _priceService = priceService;
            _promotionRepo = promotionRepo;
            _promotionEngine = promotionEngine;
            _loyaltyRepo = loyaltyRepo;
            _loyaltyTierRepo = loyaltyTierRepo;
            _loyaltyTransactionRepo = loyaltyTransactionRepo;
            _branchServiceRepo = branchServiceRepo;
            _courtRepo = courtRepo;
            _userBranchRepo = userBranchRepo;
            _userRepo = userRepo;
            _timeSlotRepo = timeSlotRepo;
            _paymentStarter = paymentStarter;
            _emailService = emailService;
            _codeGeneratorService = codeGeneratorService;
            _slotInterestRepo = slotInterestRepo;
            _notifications = notifications;
            _unitOfWork = unitOfWork;
            _logger = logger;
            _configuration = configuration;
            _broadcast = broadcast;
            _depositService = depositService;
            _paymentSettings = paymentSettings;
            _timeProvider = timeProvider;
        }
        public async Task<BookingDto> GetByIdAsync(
            Guid bookingId,
            Guid userId,
            string userRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);

            if (booking == null)
            {
                throw new AppException(
                    404, "Không tìm thấy booking", ErrorCodes.NotFound);
            }

            // CUSTOMER chỉ được xem booking của chính mình
            if (userRole == UserRole.CUSTOMER.ToString() && booking.CustomerId != userId)
            {
                throw new AppException(403,
                    "Bạn không có quyền xem đơn đặt sân này",
                    ErrorCodes.Forbidden);
            }

            // STAFF/MANAGER phải thuộc chi nhánh mới được xem
            await ValidateBranchAccessAsync(
                booking.BranchId,
                userId,
                userRole);

            return MapToDto(booking, IsStaffView(userRole));
        }
        // Lấy danh sách booking theo quyền + chi nhánh + filter 
        public async Task<PagedResult<BookingDto>> GetAllAsync(
            BookingListQuery query, Guid currentUserId, string currentUserRole)
        {
            var pagedResult = await _bookingRepo.GetAllAsync(
                query, currentUserRole, currentUserId);

            return new PagedResult<BookingDto>
            {
                Items = pagedResult.Items.Select(b => MapToDto(b, IsStaffView(currentUserRole))),
                TotalItems = pagedResult.TotalItems,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize
            };
        }
        /// <summary>
        /// Lấy lịch booking theo sân trong một ngày.
        /// Delegate xuống Repository layer để query database.
        /// </summary>
        /// <param name="query">Query parameters từ client</param>
        /// <param name="currentUserId">User ID hiện tại (từ JWT token)</param>
        /// <param name="currentUserRole">Role hiện tại (OWNER, BRANCH_MANAGER, STAFF)</param>
        /// <returns>Danh sách sân kèm lịch booking trong ngày</returns>
        /// <remarks>
        /// Service layer chỉ delegate xuống Repository, không có business logic phức tạp.
        /// Branch scoping được xử lý ở Repository layer.
        /// </remarks>
        public async Task<List<BookingScheduleCourtDto>> GetScheduleAsync(
            BookingScheduleQuery query, Guid currentUserId, string currentUserRole)
        {
            return await _bookingRepo.GetScheduleAsync(query, currentUserRole, currentUserId);
        }

        /// <summary>
        /// Lấy thống kê nhanh cho dashboard booking.
        /// Delegate xuống Repository layer để query database.
        /// </summary>
        /// <param name="query">Query parameters từ client</param>
        /// <param name="currentUserId">User ID hiện tại (từ JWT token)</param>
        /// <param name="currentUserRole">Role hiện tại (OWNER, BRANCH_MANAGER, STAFF)</param>
        /// <returns>Thống kê tổng quan booking hôm nay</returns>
        /// <remarks>
        /// Service layer chỉ delegate xuống Repository, không có business logic phức tạp.
        /// Branch scoping được xử lý ở Repository layer.
        /// </remarks>
        public async Task<BookingDashboardSummaryDto> GetDashboardSummaryAsync(
            BookingDashboardSummaryQuery query, Guid currentUserId, string currentUserRole)
        {
            return await _bookingRepo.GetDashboardSummaryAsync(query, currentUserRole, currentUserId);
        }

        /// <summary>
        /// Lấy dữ liệu heatmap booking theo tháng.
        /// Validate input và delegate xuống Repository layer.
        /// </summary>
        /// <param name="query">Query parameters từ client</param>
        /// <param name="currentUserId">User ID hiện tại (từ JWT token)</param>
        /// <param name="currentUserRole">Role hiện tại (OWNER, BRANCH_MANAGER, STAFF)</param>
        /// <returns>Danh sách dữ liệu booking theo từng ngày trong tháng</returns>
        /// <remarks>
        /// Service layer xử lý default values cho Year và Month nếu client không truyền.
        /// Branch scoping được xử lý ở Repository layer.
        /// </remarks>
        public async Task<List<BookingCalendarHeatmapDto>> GetCalendarHeatmapAsync(
            BookingCalendarHeatmapQuery query, Guid currentUserId, string currentUserRole)
        {
            // Default Year và Month nếu không truyền
            if (query.Year == 0)
                query.Year = DateTimeHelper.GetTodayInVietnam().Year;

            if (query.Month == 0)
                query.Month = DateTimeHelper.GetTodayInVietnam().Month;

            return await _bookingRepo.GetCalendarHeatmapAsync(query, currentUserRole, currentUserId);
        }

        public async Task<PagedResult<BookingDto>> GetMyBookingsAsync(
            Guid customerId, BookingListQuery query)
        {
            var pagedResult = await _bookingRepo.GetByCustomerIdAsync(customerId, query);

            return new PagedResult<BookingDto>
            {
                Items = pagedResult.Items.Select(b => MapToDto(b, includeStaffOnlyFields: false)),
                TotalItems = pagedResult.TotalItems,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize
            };
        }

        // Lấy thông tin booking theo id, có phân quyền
        public async Task<BookingDto> GetDetailsByIdAsync(
            Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            // CUSTOMER chỉ xem booking của chính mình
            // — Booking của guest (CustomerId = null) → customer không thể xem
            // — Booking của customer khác → 403
            if (currentUserRole == UserRole.CUSTOMER.ToString())
            {
                if (!booking.CustomerId.HasValue ||
                    booking.CustomerId.Value != currentUserId)
                    throw new AppException(403,
                        "Bạn không có quyền xem đơn này", ErrorCodes.Forbidden);

                return MapToDetailDto(booking, includeStaffOnlyFields: false);
            }

            // MANAGER/STAFF chỉ xem chi nhánh mình
            if (currentUserRole == UserRole.BRANCH_MANAGER.ToString() ||
                currentUserRole == UserRole.STAFF.ToString())
            {
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(
                    currentUserId, booking.BranchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Bạn không có quyền xem đơn này", ErrorCodes.Forbidden);
            }

            return MapToDetailDto(booking, includeStaffOnlyFields: true);
        }
        // ────────────────────────────────────────────────────────────────────────────
        // 1. CreateOnlineAsync
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Đặt sân online. Transaction 1 giữ chỗ (booking PENDING, hóa đơn, slot lock, chưa có Payment); hỏi quyết định cọc ngoài
        /// transaction; transaction 2 áp nhánh xử lý bằng câu cập nhật có điều kiện status = PENDING.
        /// </summary>
        public async Task<OnlineBookingResponse> CreateOnlineAsync(
            CreateOnlineBookingDto dto, Guid customerId)
        {
            if (!dto.Courts.Any())
                throw new AppException(400,
                    "Vui lòng chọn ít nhất 1 sân", ErrorCodes.BadRequest);

            // 1. Khung giờ đã bắt đầu thì chặn trước mọi truy vấn (Validate của DTO chỉ so ngày).
            //    Cùng một "bây giờ" cho created_at, hạn giữ chỗ và token hủy.
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var bookingDate = DateOnly.FromDateTime(dto.BookingDate);
            var playStartUtc = DateTimeHelper.ToUtcFromVietnam(
                bookingDate, TimeOnly.FromTimeSpan(dto.Courts.Min(c => c.StartTime)));
            if (playStartUtc <= now)
                throw new AppException(400,
                    "Khung giờ đã bắt đầu hoặc đã qua, vui lòng chọn khung giờ khác", ErrorCodes.BadRequest);

            // 2. Load + validate tất cả courts — fail fast trước khi tạo bất kỳ record nào
            var courtEntities = new List<(CourtSlotDto Slot, Court Court)>();

            var courtIds = dto.Courts.Select(c => c.CourtId).Distinct().ToList();
            var courtsFromDb = await _courtRepo.GetByIdsAsync(courtIds);
            var courtDict = courtsFromDb.ToDictionary(c => c.Id);

            foreach (var courtSlot in dto.Courts)
            {
                if (!courtDict.TryGetValue(courtSlot.CourtId, out var court))
                    throw new AppException(404,
                        $"Không tìm thấy sân {courtSlot.CourtId}", ErrorCodes.NotFound);

                if (court.Status == CourtStatus.SUSPENDED)
                    throw new AppException(400,
                        $"Sân {court.Name} đang tạm ngưng hoạt động", ErrorCodes.BadRequest);

                if (court.Status == CourtStatus.INACTIVE)
                    throw new AppException(400,
                        $"Sân {court.Name} không còn hoạt động", ErrorCodes.BadRequest);

                if (courtEntities.Any() &&
                    court.BranchId != courtEntities.First().Court.BranchId)
                    throw new AppException(400,
                        "Tất cả sân phải thuộc cùng 1 chi nhánh", ErrorCodes.BadRequest);

                courtEntities.Add((courtSlot, court));
            }

            var branchId = courtEntities.First().Court.BranchId;

            // 3. Check overlap + slot_lock TRƯỚC transaction.
            //    Nếu slot thật sự unavailable ở đây → ghi slot_interest ngay (connection sạch).
            await _slotLockRepo.DeleteExpiredByBranchAsync(branchId);

            foreach (var (slot, court) in courtEntities)
            {
                var hasOverlap = await _bookingRepo.HasOverlapAsync(
                    slot.CourtId, bookingDate,
                    TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                if (hasOverlap)
                    throw await CreateSlotUnavailableExceptionAsync(
                        dto, customerId, dto.Courts,
                        $"Sân {court.Name} đã được đặt trong khung giờ này");

                var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                    slot.CourtId, bookingDate,
                    TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                if (existingLock != null)
                    throw await CreateSlotUnavailableExceptionAsync(
                        dto, customerId, dto.Courts,
                        $"Sân {court.Name} đang trong quá trình thanh toán");
            }

            // Hạn thanh toán và hạn giữ chỗ tuyệt đối của đơn mới (7.5); slot lock hết hạn cùng hóa đơn
            var hold = PaymentHoldPolicy.ForNewBooking(now, _paymentSettings.CurrentValue);

            // Khai báo ngoài transaction scope để dùng được sau khi scope đóng
            Guid bookingId = Guid.Empty;
            Guid invoiceId = Guid.Empty;
            Guid? promotionId = null;
            decimal finalTotal = 0;
            string bookingCode = string.Empty;

            // Flag báo race condition được phát hiện bên trong transaction.
            // CreateSlotUnavailableExceptionAsync cần ghi DB nên phải gọi SAU KHI
            // TransactionScope.Dispose() đã issue ROLLBACK và reset PG connection.
            bool isRaceCondition = false;

            // Transaction 1 bắt đầu SAU pre-check để không bao giờ ghi slot_interest
            // bên trong một transaction đang thất bại. Read Committed: Serializable mặc định làm hai đơn đồng thời lỗi 40001.
            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // 4. Tính giá — group các sân cùng khung giờ để tối ưu số lần gọi
                decimal totalCourtFee = 0;
                var priceResults = new List<(CourtSlotDto Slot, CourtPriceResultDto Price)>();

                var groupedSlots = courtEntities
                    .GroupBy(ce => new { ce.Slot.StartTime, ce.Slot.EndTime });

                foreach (var group in groupedSlots)
                {
                    var calcResult = await _priceService.CalculateForBookingAsync(
                        branchId,
                        new CalculatePriceDto
                        {
                            Courts = group.Select(ce => ce.Slot.CourtId).ToList(),
                            BookingDate = dto.BookingDate,
                            StartTime = group.Key.StartTime,
                            EndTime = group.Key.EndTime
                        });

                    totalCourtFee += calcResult.TotalFee;

                    foreach (var ce in group)
                    {
                        var courtResult = calcResult.Courts.First(c => c.CourtId == ce.Slot.CourtId);
                        priceResults.Add((ce.Slot, courtResult));
                    }
                }

                // 5. Loyalty discount tính trên tổng court fee
                decimal loyaltyDiscountAmount = 0;
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(customerId);
                if (loyalty?.Tier != null)
                    loyaltyDiscountAmount = Math.Round(
                        Math.Clamp(totalCourtFee * loyalty.Tier.DiscountRate / 100, 0, totalCourtFee), 0);

                var totalAfterLoyalty = Math.Max(0, totalCourtFee - loyaltyDiscountAmount);

                // 6. Promotion discount
                var (promotion, promotionDiscountAmount) = await ValidateAndApplyPromotionAsync(
                    dto.PromotionId, customerId, branchId,
                    courtEntities, dto.BookingDate, totalAfterLoyalty);

                finalTotal = Math.Max(0, totalAfterLoyalty - promotionDiscountAmount);

                // 7. Tạo booking PENDING; đặt online luôn có khách đăng nhập nên các trường guest* của DTO bị bỏ qua
                //    Dùng UTC để Npgsql lưu timestamptz đúng. Frontend tự convert sang VN time.
                bookingCode = await _codeGeneratorService.GenerateBookingCodeAsync();
                var booking = await _bookingRepo.CreateAsync(new Booking
                {
                    BookingCode = bookingCode,
                    BranchId = branchId,
                    CustomerId = customerId,
                    BookingDate = bookingDate,
                    Status = BookingStatus.PENDING,
                    Source = BookingSource.ONLINE,
                    Note = dto.Note?.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now
                });

                // 8–11. Tất cả DB writes có thể kích hoạt exclusion constraint.
                //
                //  *** ROOT CAUSE của bug race-condition 500 ***
                //  Constraint 23P01 (excl_booking_courts_no_overlap) bắn tại SaveChangesAsync()
                //  bên trong CreateBookingDetailsAsync → AddCourtAsync, KHÔNG phải tại
                //  transaction.Complete(). Try-catch chỉ bọc Complete() sẽ không bắt được.
                //  → Phải bọc từ CreateBookingDetailsAsync trở đi.
                try
                {
                    // 8–10. BookingCourt INSERT — đây là điểm 23P01 nổ khi race condition
                    var invoice = await CreateBookingDetailsAsync(
                        booking,
                        booking.BookingDate,
                        priceResults,
                        promotion,
                        promotionDiscountAmount,
                        totalCourtFee,
                        loyaltyDiscountAmount,
                        finalTotal,
                        PaymentTiming.PREPAID, // Transaction 2 đổi sang POSTPAID nếu đơn không thanh toán online (a′)
                        hold);

                    // 11. SlotLock — một khóa mỗi sân, hết hạn cùng hóa đơn; transaction 2 xóa (a′, c) hoặc gia hạn (b).
                    //     Court.Status KHÔNG thay đổi ở đây; scheduled job cleanup khi PENDING expire.
                    foreach (var (slot, _) in courtEntities)
                    {
                        await _slotLockRepo.CreateAsync(new SlotLock
                        {
                            CourtId = slot.CourtId,
                            BookingId = booking.Id,
                            Date = bookingDate,
                            StartTime = TimeOnly.FromTimeSpan(slot.StartTime),
                            EndTime = TimeOnly.FromTimeSpan(slot.EndTime),
                            ExpiresAt = hold.ExpiresAt,
                            CreatedAt = now
                        });
                    }

                    // 12. COMMIT transaction 1 — chưa có Payment: số tiền thu online chỉ biết sau quyết định cọc
                    transaction.Complete();
                    bookingId = booking.Id;
                    invoiceId = invoice.Id;
                    promotionId = promotion?.Id;
                }
                catch (Exception ex) when (IsOverlapConstraintViolation(ex))
                {
                    // Race condition: slot bị book bởi request khác trong khoảnh khắc này.
                    // KHÔNG rethrow ở đây — để using block thoát bình thường và gọi Dispose(),
                    // Dispose() sẽ issue ROLLBACK + reset PG connection về trạng thái sạch.
                    // CreateSlotUnavailableExceptionAsync (có ghi DB) sẽ được gọi SAU using block.
                    _unitOfWork.ClearChangeTracker();
                    isRaceCondition = true;
                }
            } // ← TransactionScope.Dispose() chạy tại đây: ROLLBACK + PG connection reset

            // Race condition handler — gọi NGOÀI transaction, PG connection đã hoàn toàn sạch
            if (isRaceCondition)
            {
                throw await CreateSlotUnavailableExceptionAsync(
                    dto,
                    customerId,
                    dto.Courts,
                    "Sân đã được đặt bởi người khác, vui lòng chọn slot khác");
            }

            // Transaction 2 ghi bằng ExecuteUpdate (bỏ qua change tracker): bỏ entity của transaction 1 để lần đọc sau lấy trạng thái thật
            _unitOfWork.ClearChangeTracker();

            // 13. Quyết định cọc NGOÀI transaction (không giữ kết nối DB trong lúc chờ TADSS).
            //     Transaction 1 đã commit nên bước này và transaction 2 luôn chạy tới cùng, không dừng khi khách ngắt kết nối:
            //     dừng giữa chừng để lại đơn PENDING không cọc, giữ sân vô ích. Thời gian chờ do TimeoutMs của TadssClient giới hạn.
            //     DecideAsync không ném khi TADSS lỗi (trả phương án dự phòng) nên không cần try/catch.
            var decision = await _depositService.DecideAsync(bookingId, finalTotal, playStartUtc, CancellationToken.None);

            // 14. Transaction 2 theo nhánh; đơn đã đổi trạng thái ⇒ 409
            var applied = await ApplyDepositDecisionAsync(
                bookingId, invoiceId, decision, courtEntities, playStartUtc, now, hold.ExpiresAt, promotionId);

            // 15. Đọc lại đơn sau transaction 2 cho email và sự kiện — ngoài transaction, lỗi chỉ log, không ảnh hưởng đến booking đã ghi
            Booking? fullBooking = null;
            try
            {
                fullBooking = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load online booking {BookingId} for notifications", bookingId);
            }

            // 16. Email và sự kiện theo trạng thái cuối
            if (fullBooking != null)
                await _notifications.NotifyOnlineBookingAsync(fullBooking, decision.Branch, applied.RawCancelToken);

            // 17. TADSS từ chối: đơn đã hủy và nhả sân trong transaction 2; không ghi slot_interest vì slot vẫn trống
            if (decision.Branch == DepositBranch.Rejected)
                throw new AppException(400,
                    "Rất tiếc, đơn đặt sân này không được chấp nhận. Vui lòng chọn khung giờ khác hoặc liên hệ chi nhánh.",
                    ErrorCodes.BookingRejected);

            // 18. Response theo trạng thái cuối; số cọc lấy nguyên từ quyết định, BE không tự quy đổi
            var status = decision.Branch switch
            {
                DepositBranch.NeedApproval => BookingStatus.PENDING_APPROVAL,
                DepositBranch.ConfirmWithoutOnlinePayment => BookingStatus.CONFIRMED,
                _ => BookingStatus.PENDING
            };
            DepositFields? deposit = decision.Branch == DepositBranch.NeedApproval
                ? null
                : new DepositFields(
                    decision.DepositRate,
                    decision.DepositAmount,
                    DepositFieldsMapper.RemainingAmount(finalTotal, decision.DepositAmount),
                    RequireDepositSource(decision));

            return new OnlineBookingResponse
            {
                BookingId = bookingId,
                BookingCode = bookingCode,
                Status = status.ToString(),
                PaymentUrl = applied.PaymentUrl,
                // Nhánh a/d giữ hạn thanh toán của transaction 1; a′, b không thanh toán online nên không có hạn
                ExpiresAt = applied.PaymentUrl != null ? hold.ExpiresAt : null,
                PaymentLinkExpiresAt = applied.PaymentLinkExpiresAt,
                FinalTotal = finalTotal,
                DepositRate = deposit?.Rate,
                DepositAmount = deposit?.Amount,
                RemainingAmount = deposit?.RemainingAmount,
                DepositSource = deposit?.Source.ToString(),
                ApprovalExpiresAt = decision.ApprovalExpiresAtUtc
            };
        }

        // Kết quả transaction 2 cần cho phần sau commit: link VNPay kèm hạn của link, hoặc token hủy dạng thô để gửi email xác nhận
        private sealed record OnlineDecisionApplied(string? PaymentUrl, DateTime? PaymentLinkExpiresAt, string? RawCancelToken);

        /// <summary>
        /// Transaction 2 của đặt sân online: câu cập nhật có điều kiện status = PENDING trước, lệnh phụ sau, cùng transaction.
        /// Thua (nhân viên đã hủy đơn trong lúc chờ quyết định) ⇒ không Payment, không tăng lượt khuyến mãi, 409.
        /// </summary>
        private async Task<OnlineDecisionApplied> ApplyDepositDecisionAsync(
            Guid bookingId,
            Guid invoiceId,
            DepositDecision decision,
            List<(CourtSlotDto Slot, Court Court)> courtEntities,
            DateTime playStartUtc,
            DateTime now,
            DateTime paymentExpiresAt,
            Guid? promotionId)
        {
            // Cùng mốc đã dùng để quyết định "còn hạn duyệt" (P6-24); hạn trôi qua trong vài ms sau đó do job hết hạn duyệt dọn
            var decidedAt = decision.DecidedAtUtc;
            if (decidedAt.Kind != DateTimeKind.Utc)
                throw new InvalidOperationException($"Quyết định cọc của booking {bookingId} chưa có mốc nhận (UTC).");
            string? paymentUrl = null;
            DateTime? paymentLinkExpiresAt = null;
            string? rawCancelToken = null;
            bool won;

            // Read Committed an toàn vì mọi thay đổi trạng thái ở đây đi qua câu cập nhật có điều kiện
            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                switch (decision.Branch)
                {
                    // Cọc của TADSS hoặc dự phòng 100 %: ghi mức cọc, tạo Payment PENDING = số cọc kèm link VNPay
                    case DepositBranch.TadssDeposit:
                    case DepositBranch.FullPaymentFallback:
                        won = await _bookingRepo.ApplyPaymentRequiredAsync(
                            bookingId, decision.TadssDecisionId, decision.DepositRate, decision.DepositAmount,
                            RequireDepositSource(decision), decidedAt);
                        if (won)
                        {
                            paymentLinkExpiresAt = NewBookingLinkExpiry(decidedAt, paymentExpiresAt);
                            paymentUrl = await CreateDepositPaymentAsync(
                                bookingId, invoiceId, decision.DepositAmount, courtEntities, decidedAt, paymentLinkExpiresAt.Value);
                        }
                        break;

                    // Xác nhận ngay, thu tại quầy: token hủy ghi cùng câu cập nhật, khuyến mãi tính lượt ngay vì không có IPN
                    case DepositBranch.ConfirmWithoutOnlinePayment:
                        var cancelToken = CancelTokenHelper.Generate(playStartUtc, now);
                        won = await _bookingRepo.ConfirmWithoutOnlinePaymentAsync(
                            bookingId, decision.TadssDecisionId, RequireDepositSource(decision),
                            cancelToken.TokenHash, cancelToken.ExpiresAtUtc, decidedAt);
                        if (won)
                        {
                            rawCancelToken = cancelToken.RawToken;
                            if (promotionId is { } usedPromotionId)
                                await _promotionEngine.IncrementUsageCountAsync(usedPromotionId);
                        }
                        break;

                    // Chờ Quản lý duyệt: slot lock gia hạn tới hạn duyệt, chưa có mức cọc
                    case DepositBranch.NeedApproval:
                        if (decision is not
                            {
                                TadssDecisionId: { } approvalDecisionId,
                                ApprovalExpiresAtUtc: { } approvalExpiresAt,
                                ApprovalReasonCode: { } approvalReasonCode
                            })
                            throw new InvalidOperationException(
                                $"Quyết định NEED_APPROVAL của booking {bookingId} thiếu decisionId, hạn duyệt hoặc mã lý do.");
                        won = await _bookingRepo.HoldForApprovalAsync(
                            bookingId, approvalDecisionId, approvalExpiresAt, approvalReasonCode,
                            decision.ApprovalReasons, decidedAt);
                        break;

                    // TADSS từ chối: hủy POLICY_REJECTED, nhả sân; caller trả 400 BOOKING_REJECTED
                    case DepositBranch.Rejected:
                        if (decision.TadssDecisionId is not { } rejectDecisionId)
                            throw new InvalidOperationException(
                                $"Quyết định REJECTED của booking {bookingId} thiếu decisionId.");
                        won = await _bookingRepo.RejectByPolicyAsync(bookingId, rejectDecisionId, decidedAt);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(decision), decision.Branch, "Nhánh quyết định cọc không hợp lệ.");
                }

                if (won)
                    transaction.Complete();
            }

            if (!won)
            {
                // Giữa hai transaction chỉ nhân viên hủy chạm được đơn PENDING chưa có Payment; đơn lúc này đã bị hủy nên không trả 201
                var currentStatus = await _bookingRepo.GetBookingStatusAsync(bookingId);
                _logger.LogError(
                    "Transaction 2 thua câu cập nhật có điều kiện: booking {BookingId} không còn PENDING khi áp nhánh {Branch} (trạng thái hiện tại {Status}); " +
                    "không tạo Payment, không tăng lượt khuyến mãi, không phát sự kiện",
                    bookingId, decision.Branch, currentStatus);
                throw new AppException(409,
                    "Đơn đặt sân vừa được nhân viên xử lý, vui lòng xem lịch sử đặt sân", ErrorCodes.Conflict);
            }

            return new OnlineDecisionApplied(paymentUrl, paymentLinkExpiresAt, rawCancelToken);
        }

        // Hạn link của đơn mới = hạn thanh toán − khoảng đệm (P6-27). Validator buộc InitialHoldMinutes ≥ khoảng đệm + 1' nên với cấu hình
        // hợp lệ luôn đủ thời gian; thiếu là lỗi cấu hình ⇒ transaction 2 rollback, đơn PENDING không Payment được Job-01 dọn
        private DateTime NewBookingLinkExpiry(DateTime now, DateTime paymentExpiresAt) =>
            PaymentHoldPolicy.TryGetPaymentLinkExpiry(
                now, paymentExpiresAt, _paymentSettings.CurrentValue.PaymentLinkBufferMinutes, out var linkExpiresAt)
                ? linkExpiresAt
                : throw new InvalidOperationException(
                    $"Không đủ thời gian tạo link thanh toán cho đơn mới (hạn thanh toán {paymentExpiresAt:O}, lúc {now:O}); kiểm cấu hình BookingPayment.");

        // Nhánh a, a′, d luôn có nguồn chính sách cọc (DepositDecisionResolver); thiếu là lỗi lập trình
        private static DepositSource RequireDepositSource(DepositDecision decision) =>
            decision.DepositSource ?? throw new InvalidOperationException(
                $"Quyết định nhánh {decision.Branch} thiếu nguồn chính sách cọc.");

        // Payment PENDING = số tiền thu online của nhánh (cọc TADSS hoặc 100 % dự phòng); IPN so vnp_Amount với Payment.Amount
        private async Task<string> CreateDepositPaymentAsync(
            Guid bookingId,
            Guid invoiceId,
            decimal amount,
            List<(CourtSlotDto Slot, Court Court)> courtEntities,
            DateTime now,
            DateTime linkExpiresAt)
        {
            var courtNames = string.Join(", ", courtEntities.Select(x => x.Court.Name).Distinct());
            // Đặt online hiện chỉ thanh toán qua VNPay
            return await _paymentStarter.StartAsync(
                invoiceId, bookingId.ToString(), amount, $"Dat san {StringHelper.RemoveDiacritics(courtNames)}",
                PaymentTxMethod.VNPAY, now, linkExpiresAt);
        }

        /// <summary>
        /// Đặt sân trực tiếp tại quầy. Luôn tạo booking ở trạng thái CONFIRMED.
        /// </summary>
        public async Task<BookingDto> CreateWalkInAsync(
            CreateWalkInBookingDto dto, Guid createdBy)
        {
            if (!dto.Courts.Any())
                throw new AppException(400,
                    "Vui lòng chọn ít nhất 1 sân", ErrorCodes.BadRequest);

            // 1. Load + validate tất cả courts
            var courtEntities = new List<(CourtSlotDto Slot, Court Court)>();

            var courtIds = dto.Courts.Select(c => c.CourtId).Distinct().ToList();
            var courtsFromDb = await _courtRepo.GetByIdsAsync(courtIds);
            var courtDict = courtsFromDb.ToDictionary(c => c.Id);

            foreach (var courtSlot in dto.Courts)
            {
                if (!courtDict.TryGetValue(courtSlot.CourtId, out var court))
                    throw new AppException(404,
                        $"Không tìm thấy sân {courtSlot.CourtId}", ErrorCodes.NotFound);

                if (court.Status == CourtStatus.SUSPENDED)
                    throw new AppException(400,
                        $"Sân {court.Name} đang tạm ngưng hoạt động", ErrorCodes.BadRequest);

                if (court.Status == CourtStatus.INACTIVE)
                    throw new AppException(400,
                        $"Sân {court.Name} không còn hoạt động", ErrorCodes.BadRequest);

                if (courtEntities.Any() &&
                    court.BranchId != courtEntities.First().Court.BranchId)
                    throw new AppException(400,
                        "Tất cả sân phải thuộc cùng 1 chi nhánh", ErrorCodes.BadRequest);

                courtEntities.Add((courtSlot, court));
            }

            var branchId = courtEntities.First().Court.BranchId;

            var user = await _userRepo.GetUserByIdAsync(createdBy);
            if (user == null)
                throw new AppException(404, "Không tìm thấy người dùng", ErrorCodes.NotFound);

            if (user.Role != UserRole.OWNER)
            {
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(createdBy, branchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Bạn không có quyền đặt sân tại chi nhánh này", ErrorCodes.Forbidden);
            }

            Guid bookingId;

            // Read Committed như transaction 1 của đặt online: trùng sân do exclusion constraint chặn (23P01/40P01)
            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // 2. Check slot_lock + overlap
                await _slotLockRepo.DeleteExpiredByBranchAsync(branchId);

                foreach (var (slot, court) in courtEntities)
                {
                    var hasOverlap = await _bookingRepo.HasOverlapAsync(
                        slot.CourtId, DateOnly.FromDateTime(dto.BookingDate),
                        TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                    if (hasOverlap)
                        throw new AppException(400,
                            $"Sân {court.Name} đã được đặt trong khung giờ này",
                            ErrorCodes.BadRequest);

                    var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                        slot.CourtId, DateOnly.FromDateTime(dto.BookingDate),
                        TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                    if (existingLock != null)
                    {
                        // ExpiresAt lưu UTC → so sánh với UTC
                        var remaining = (int)(existingLock.ExpiresAt - DateTime.UtcNow).TotalMinutes;
                        throw new AppException(400,
                            $"Sân {court.Name} đang bị khóa thanh toán ({remaining} phút)",
                            ErrorCodes.BadRequest);
                    }
                }

                // 3. Tính giá — group các sân cùng khung giờ
                decimal totalCourtFee = 0;
                var priceResults = new List<(CourtSlotDto Slot, CourtPriceResultDto Price)>();

                var groupedSlots = courtEntities
                    .GroupBy(ce => new { ce.Slot.StartTime, ce.Slot.EndTime });

                foreach (var group in groupedSlots)
                {
                    var calcResult = await _priceService.CalculateForBookingAsync(
                        branchId,
                        new CalculatePriceDto
                        {
                            Courts = group.Select(ce => ce.Slot.CourtId).ToList(),
                            BookingDate = dto.BookingDate,
                            StartTime = group.Key.StartTime,
                            EndTime = group.Key.EndTime
                        });

                    totalCourtFee += calcResult.TotalFee;

                    foreach (var ce in group)
                    {
                        var courtResult = calcResult.Courts.First(c => c.CourtId == ce.Slot.CourtId);
                        priceResults.Add((ce.Slot, courtResult));
                    }
                }

                // 4. Loyalty + promotion
                decimal loyaltyDiscountAmount = 0;
                if (dto.CustomerId.HasValue)
                {
                    var loyalty = await _loyaltyRepo.GetByUserIdAsync(dto.CustomerId.Value);
                    if (loyalty?.Tier != null)
                        loyaltyDiscountAmount = Math.Round(
                            totalCourtFee * loyalty.Tier.DiscountRate / 100, 0);
                }

                var totalAfterLoyalty = totalCourtFee - loyaltyDiscountAmount;

                var (promotion, promotionDiscountAmount) = await ValidateAndApplyPromotionAsync(
                    dto.PromotionId, dto.CustomerId, branchId,
                    courtEntities, dto.BookingDate, totalAfterLoyalty);

                var finalTotal = totalAfterLoyalty - promotionDiscountAmount;

                // 5. PaymentTiming
                var paymentTiming = dto.PayNow ? PaymentTiming.PREPAID : PaymentTiming.POSTPAID;

                // 6. Tạo booking CONFIRMED
                var bookingCode = await _codeGeneratorService.GenerateBookingCodeAsync();
                var booking = new Booking
                {
                    BookingCode = bookingCode,
                    BranchId = branchId,
                    CustomerId = dto.CustomerId,
                    GuestName = dto.GuestName?.Trim(),
                    GuestPhone = dto.GuestPhone?.Trim(),
                    GuestEmail = dto.GuestEmail?.Trim(),
                    BookingDate = DateOnly.FromDateTime(dto.BookingDate),
                    Status = BookingStatus.CONFIRMED,
                    Source = BookingSource.WALK_IN,
                    Note = dto.Note?.Trim(),
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                booking = await _bookingRepo.CreateAsync(booking);

                // 7–13. Tất cả DB writes có thể kích hoạt exclusion constraint.
                //
                //  *** ROOT CAUSE của bug race-condition 500 ***
                //  Constraint 23P01 bắn tại SaveChangesAsync() trong AddCourtAsync,
                //  KHÔNG phải tại transaction.Complete().
                //  → Phải bọc từ CreateBookingDetailsAsync trở đi.
                try
                {
                    // 7–9. BookingCourt INSERT — đây là điểm 23P01 nổ khi race condition
                    var invoice = await CreateBookingDetailsAsync(
                        booking,
                        booking.BookingDate,
                        priceResults,
                        promotion,
                        promotionDiscountAmount,
                        totalCourtFee,
                        loyaltyDiscountAmount,
                        finalTotal,
                        paymentTiming);

                    // 10. PREPAID: cập nhật invoice, tạo payment, tăng promotion usage
                    if (dto.PayNow)
                    {
                        invoice.PaymentStatus = InvoicePaymentStatus.PAID;
                        await _invoiceRepo.UpdateAsync(invoice);

                        await _paymentRepo.CreateAsync(new Payment
                        {
                            InvoiceId = invoice.Id,
                            Method = PaymentTxMethod.CASH,
                            Amount = finalTotal,
                            Status = PaymentTxStatus.SUCCESS,
                            PaidAt = DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });

                        if (promotion != null)
                        {
                            await _promotionEngine.IncrementUsageCountAsync(promotion.Id);
                            _logger.LogInformation(
                                "[PROMOTION_USAGE] Incremented usage for walk-in booking " +
                                "| PromotionId={PromotionId} | BookingId={BookingId}",
                                promotion.Id, booking.Id);
                        }
                    }

                    // 11. Court status được update bởi scheduled job khi đến StartTime.
                    //     KHÔNG update ở đây để cho phép overbooking nếu cần.

                    // 12. COMMIT
                    bookingId = booking.Id;
                    transaction.Complete();
                }
                catch (Exception ex) when (IsOverlapConstraintViolation(ex))
                {
                    // AppException là object thuần — không có DB ops → an toàn throw trong catch.
                    // using block sẽ bắt exception này khi unwind, gọi Dispose() → ROLLBACK.
                    _logger.LogWarning(ex,
                        "EXCLUDE constraint violated — race condition detected for walk-in booking");
                    _unitOfWork.ClearChangeTracker();
                    throw new AppException(400,
                        "Sân đã được đặt bởi người khác, vui lòng chọn slot khác",
                        ErrorCodes.BadRequest);
                }
            } // ← TransactionScope.Dispose(): ROLLBACK nếu Complete() chưa được gọi thành công

            // 13. Query booking details NGOÀI transaction — connection đã sạch
            var result = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);

            // 14. Email xác nhận — lỗi email không được ảnh hưởng booking đã tạo thành công
            var shouldSendEmail = dto.PayNow
                || !string.IsNullOrEmpty(result!.Customer?.Email)
                || !string.IsNullOrEmpty(result.GuestEmail);

            if (shouldSendEmail)
            {
                try
                {
                    await SendConfirmationEmailAsync(
                        result!,
                        courtEntities.Select(c => (c.Slot, c.Court)).ToList());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to send confirmation email for booking {Id}", bookingId);
                }
            }

            // 15. SignalR notification — lỗi không ảnh hưởng booking
            try
            {
                if (result != null)
                {
                    var customerName = result.Customer?.FullName ?? result.GuestName ?? "Khách";
                    // includeTimeGrid=true: walk-in booking holds slot
                    await _broadcast.BroadcastBookingEventAsync(
                        SignalREvents.BookingCreated,
                        new BookingNotificationDto
                        {
                            BookingId = result.Id,
                            CustomerId = result.CustomerId ?? Guid.Empty,
                            CustomerName = customerName,
                            BranchId = result.BranchId,
                            BranchName = result.Branch?.Name ?? "",
                            CourtIds = result.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                            Status = result.Status.ToString(),
                            Message = $"Booking tại quầy #{result.BookingCode} đã được tạo thành công.",
                            Timestamp = DateTimeHelper.GetUtcNow()
                        },
                        result,
                        includeTimeGrid: true);
                }
            }
            catch (Exception ex)
            {
                if (result != null)
                    _logger.LogError(ex,
                        "Failed to send SignalR notification for walk-in booking {BookingId}", result.Id);
            }

            return MapToDto(result!, includeStaffOnlyFields: true);
        }
        // Thu tiền cho booking đang chờ (PENDING_PAYMENT) và hoàn tất đơn
        public async Task CollectPaymentAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            // Reuse Checkout logic which already handles PENDING_PAYMENT and IN_PROGRESS states,
            // creates necessary Payment records and updates invoice/booking status.
            await CheckoutAsync(id, currentUserId, currentUserRole);
        }

        // Check-in khách hàng đến sân, chỉ cho phép check-in khi booking đang CONFIRMED hoặc PAID_ONLINE
        public async Task CheckInAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);
            var bookingCourt = booking.BookingCourts.FirstOrDefault();
            if (bookingCourt == null)
                throw new AppException(500, "Không tìm thấy sân và khung giờ đã đặt", ErrorCodes.InternalError);

            var date = booking.BookingDate;

            var startLocal = date.ToDateTime(bookingCourt.StartTime);

            var startDateTime = TimeZoneInfo.ConvertTimeToUtc(startLocal, DateTimeHelper.VNTimezone);

            var now = DateTimeHelper.GetUtcNow();

            if (now < startDateTime.AddMinutes(-15))
                throw new AppException(400, "Quá sớm để check-in", ErrorCodes.BadRequest);

            if (now > startDateTime.AddMinutes(15))
                throw new AppException(400, "Đã quá thời gian check-in", ErrorCodes.BadRequest);

            if (booking.Status != BookingStatus.CONFIRMED &&
                booking.Status != BookingStatus.PAID_ONLINE)
                throw new AppException(400,
                    "Chỉ có thể check-in đơn đang xác nhận hoặc đã thanh toán trực tuyến",
                    ErrorCodes.BadRequest);

            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                BookingStatusTransition.ValidateTransition(booking.Status, BookingStatus.IN_PROGRESS);

                // Hai lần check-in, hoặc check-in trùng lệnh hủy: chỉ một bên thắng. Link hủy hết dùng được khi khách đang chơi
                if (!await _bookingRepo.CheckInAsync(booking.Id, booking.Status, now))
                    throw new AppException(409, "Đơn đã được xử lý bởi người khác", ErrorCodes.Conflict);
                booking.Status = BookingStatus.IN_PROGRESS;
                booking.CheckedInAt = now;
                booking.UpdatedAt = now;

                // Cập nhật court → IN_USE
                // bc.Court đã được load sẵn qua GetByIdWithDetailsAsync().ThenInclude
                foreach (var bc in booking.BookingCourts)
                {
                    if (bc.Court == null) continue;
                    bc.Court.Status = CourtStatus.IN_USE;
                    bc.Court.UpdatedAt = now;
                    await _courtRepo.UpdateAsync(bc.Court);
                }

                transaction.Complete();
            }

            // Broadcast SignalR notification
            var customerName = booking.Customer?.FullName ?? booking.GuestName ?? "Khách";
            await _broadcast.BroadcastBookingEventAsync(
                SignalREvents.BookingCheckedIn,
                new BookingNotificationDto
                {
                    BookingId = booking.Id,
                    CustomerId = booking.CustomerId ?? Guid.Empty,
                    CustomerName = customerName,
                    BranchId = booking.BranchId,
                    BranchName = booking.Branch.Name,
                    CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                    Status = booking.Status.ToString(),
                    Message = $"{customerName} đã check-in tại {booking.Branch.Name}",
                    Timestamp = DateTimeHelper.GetUtcNow()
                },
                booking
            );
        }

        // Checkout khách hàng rời sân, chấp nhận IN_PROGRESS (khách về sớm) và PENDING_PAYMENT (hết giờ)
        public async Task CheckoutAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            // Cho phép checkout từ IN_PROGRESS (khách về sớm trước EndTime)
            // hoặc PENDING_PAYMENT (Job-02 đã set sau khi hết giờ)
            var checkoutableStatuses = new[]
            {
                BookingStatus.IN_PROGRESS,
                BookingStatus.PENDING_PAYMENT
            };
            if (!checkoutableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Chỉ có thể checkout đơn đang tiến hành hoặc chờ thanh toán",
                    ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

            var now = DateTime.UtcNow;
            var originalStatus = booking.Status;

            // Read Committed an toàn vì câu cập nhật có điều kiện trên booking chạy đầu tiên: thêm/bớt dịch vụ cũng khóa dòng booking
            // trước, nên hóa đơn đọc sau đó luôn là bản mới nhất
            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // 1. Chỉ một trong hai lần check-out đồng thời thắng
                var rowsAffected = await _bookingRepo.UpdateWithStatusCheckAsync(
                    booking.Id,
                    BookingStatus.COMPLETED,
                    originalStatus);

                if (rowsAffected == 0)
                    throw new AppException(409,
                        "Đơn đã được checkout bởi người khác",
                        ErrorCodes.Conflict);

                // CRITICAL: ExecuteUpdateAsync không update entity trong memory
                // Phải update thủ công để logic phía sau dùng đúng giá trị
                booking.Status = BookingStatus.COMPLETED;
                booking.UpdatedAt = now;

                // 1.5. Query lại invoice FinalTotal từ DB để đảm bảo có service mới nhất
                // Tránh race condition: Staff B add service sau khi Staff A đã load invoice
                var latestInvoice = await _invoiceRepo.GetByBookingIdAsync(booking.Id);
                if (latestInvoice == null)
                    throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

                // 2. Thu tại quầy phần còn lại: tổng hiện tại (gồm dịch vụ) trừ số đã thu, không âm
                var collected = await _paymentRepo.GetCollectedAmountAsync(latestInvoice.Id);
                var remaining = Math.Max(0m, latestInvoice.FinalTotal - collected);
                if (remaining > 0)
                {
                    await _paymentRepo.CreateAsync(new Payment
                    {
                        InvoiceId = latestInvoice.Id,
                        Method = PaymentTxMethod.CASH,
                        Amount = remaining,
                        Status = PaymentTxStatus.SUCCESS,
                        PaidAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else if (collected > latestInvoice.FinalTotal)
                {
                    // Khách trả online nhiều lần (link cũ vẫn trả được): phần thừa hoàn tiền mặt tại chi nhánh
                    _logger.LogError(
                        "Booking {BookingId} đã thu {Collected} đ, vượt tổng {FinalTotal} đ: thừa {Overpaid} đ, cần hoàn tại chi nhánh",
                        booking.Id, collected, latestInvoice.FinalTotal, collected - latestInvoice.FinalTotal);
                }

                // 3. Hóa đơn PAID (chỉ đổi trạng thái, không ghi đè các cột khác)
                await _invoiceRepo.SetPaymentStatusAsync(latestInvoice.Id, InvoicePaymentStatus.PAID, now);
                latestInvoice.PaymentStatus = InvoicePaymentStatus.PAID;

                // 3.1. Trường hợp check-out sớm, cập nhật actual_end_play_time
                // After fetching booking courts
                var nowTime = TimeOnly.FromDateTime(DateTimeHelper.GetVietnamNow());
                var isEarlyCheckout = booking.BookingCourts
                    .Any(bc => bc.IsActive && nowTime < bc.EndTime);
                if (isEarlyCheckout)
                    await _bookingRepo.SetActualEndPlayTimeAsync(booking.Id, nowTime);

                // 4. Deactivate booking courts khi COMPLETED
                await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

                // 5. Cập nhật court → AVAILABLE (batch update để tránh N+1 query)
                var courtIds = booking.BookingCourts
                    .Where(bc => bc.Court != null)
                    .Select(bc => bc.Court!.Id)
                    .ToList();

                if (courtIds.Any())
                {
                    await _courtRepo.BatchUpdateStatusAsync(
                        courtIds,
                        CourtStatus.AVAILABLE,
                        now);
                }

                // 6. Tích điểm loyalty nếu có tài khoản (atomic update bên trong)
                // Dùng invoice.CourtFee (không thay đổi) thay vì latestInvoice
                if (booking.CustomerId.HasValue)
                    await EarnLoyaltyPointsAsync(booking, invoice.CourtFee);

                // 7. Commit transaction
                transaction.Complete();
            }

            // Broadcast SignalR notification
            var customerName = booking.Customer?.FullName ?? booking.GuestName ?? "Khách";
            await _broadcast.BroadcastBookingEventAsync(
                SignalREvents.BookingCheckedOut,
                new BookingNotificationDto
                {
                    BookingId = booking.Id,
                    CustomerId = booking.CustomerId ?? Guid.Empty,
                    CustomerName = customerName,
                    BranchId = booking.BranchId,
                    BranchName = booking.Branch.Name,
                    CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                    Status = booking.Status.ToString(),
                    Message = $"Booking #{booking.BookingCode} đã hoàn tất",
                    Timestamp = DateTimeHelper.GetUtcNow()
                },
                booking
            );
        }

        // thêm dịch vụ vào booking, chỉ cho phép thêm khi booking đang active và invoice chưa thanh toán đủ
        public async Task<BookingDto> AddServiceAsync(
            Guid id, AddBookingServiceDto dto,
            Guid currentUserId, string currentUserRole)
        {
            // Validate quantity > 0
            if (dto.Quantity <= 0)
                throw new AppException(400,
                    "Số lượng phải lớn hơn 0", ErrorCodes.BadRequest);

            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

            // LAYER A: Early validation (UX / fail-fast)
            // Validate có thể chỉnh sửa dịch vụ hay không (dựa trên data đã load)
            // NOTE: Đây chỉ là early check để fail-fast, KHÔNG phải source of truth
            // Source of truth là re-check TRONG transaction (Layer B)
            if (!CanModifyServices(booking, invoice))
                throw new AppException(400,
                    "Không thể thêm dịch vụ ở trạng thái hiện tại", ErrorCodes.BadRequest);

            // Tìm branch service
            var branchService = await _branchServiceRepo.GetByBranchServiceAsync(
                booking.BranchId, dto.ServiceId);

            if (branchService == null ||
                branchService.Status != BranchServiceStatus.ENABLED ||
                branchService.Service.Status != ServiceStatus.ACTIVE)
                throw new AppException(400,
                    "Dịch vụ không tồn tại hoặc đã bị tắt", ErrorCodes.BadRequest);

            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // Khóa dòng booking trước: check-out cũng khóa dòng này, nên hóa đơn đọc dưới đây là bản mới nhất
                // và không bao giờ có dịch vụ chưa thu nằm trên hóa đơn đã PAID
                if (!await _bookingRepo.TryLockForServiceChangeAsync(booking.Id, DateTime.UtcNow))
                    throw new AppException(400,
                        "Không thể thêm/xóa dịch vụ - đơn đã kết thúc hoặc bị hủy", ErrorCodes.BadRequest);

                // LAYER B: Source of truth - Re-check TRONG transaction (CRITICAL)
                // Query fresh data từ DB và validate bằng consolidated method
                var currentStatus = await _bookingRepo.GetBookingStatusAsync(booking.Id);
                var latestInvoice = await _invoiceRepo.GetByBookingIdAsync(booking.Id);

                if (latestInvoice == null)
                    throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

                // Consolidated validation: Double protection (Status + PaymentStatus)
                EnsureBookingModifiable(currentStatus, latestInvoice.PaymentStatus);

                // Check duplicate service - nếu đã tồn tại thì tăng quantity thay vì tạo mới
                var existingService = booking.BookingServices
                    .FirstOrDefault(bs => bs.ServiceId == dto.ServiceId);

                if (existingService != null)
                {
                    // UX tốt hơn: Merge quantity bằng atomic update (tránh race condition)
                    // Dùng ExecuteUpdateAsync: UPDATE SET quantity = quantity + @delta
                    // → Không bị lost update khi 2 staff add cùng lúc
                    await _bookingRepo.UpdateServiceQuantityAtomicAsync(
                        existingService.Id, dto.Quantity);
                }
                else
                {
                    // Tạo service mới với snapshot giá + tên tại thời điểm thêm
                    var bookingService = new SmashCourt_BE.Models.Entities.BookingService
                    {
                        BookingId = booking.Id,
                        ServiceId = dto.ServiceId,
                        ServiceName = branchService.Service.Name,
                        Unit = branchService.Service.Unit,
                        UnitPrice = branchService.Price,
                        Quantity = dto.Quantity,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _bookingRepo.AddServiceAsync(bookingService);
                }

                // Tính lại service_fee từ DB (không dùng memory collection)
                // Dùng SumAsync để tối ưu performance (không load list vào memory)
                var serviceFeeTotal = await _bookingRepo.CalculateServiceFeeAsync(booking.Id);

                // Cập nhật invoice (dùng latestInvoice từ DB, không dùng invoice từ memory)
                latestInvoice.ServiceFee = serviceFeeTotal;
                latestInvoice.FinalTotal = latestInvoice.CourtFee
                                   - latestInvoice.LoyaltyDiscountAmount
                                   - latestInvoice.PromotionDiscountAmount
                                   + serviceFeeTotal;
                latestInvoice.UpdatedAt = DateTime.UtcNow;
                await RecalculatePaymentStatusAsync(latestInvoice);
                await _invoiceRepo.UpdateAsync(latestInvoice);

                // Commit transaction
                transaction.Complete();

                // Audit-grade logging: Structured log với tất cả context quan trọng
                _logger.LogInformation(
                    "SERVICE_MODIFICATION | Action={Action} | BookingId={BookingId} | ServiceId={ServiceId} | " +
                    "ServiceName={ServiceName} | Quantity={Quantity} | UnitPrice={UnitPrice} | " +
                    "UserId={UserId} | BookingStatus={BookingStatus} | PaymentStatus={PaymentStatus} | " +
                    "OldTotal={OldTotal} | NewTotal={NewTotal}",
                    "ADD", booking.Id, dto.ServiceId, branchService.Service.Name, dto.Quantity, branchService.Price,
                    currentUserId, currentStatus, latestInvoice.PaymentStatus,
                    invoice.FinalTotal, latestInvoice.FinalTotal);
            }

            // Query lại booking với details để trả về
            var result = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);

            // Broadcast SignalR notification - BookingUpdated
            try
            {
                if (result != null)
                {
                    var customerName = result.Customer?.FullName ?? result.GuestName ?? "Khách";
                    await _broadcast.BroadcastBookingEventAsync(
                        SignalREvents.BookingUpdated,
                        new BookingNotificationDto
                        {
                            BookingId = result.Id,
                            CustomerId = result.CustomerId ?? Guid.Empty,
                            CustomerName = customerName,
                            BranchId = result.BranchId,
                            BranchName = result.Branch?.Name ?? "",
                            CourtIds = result.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                            Status = result.Status.ToString(),
                            Message = $"Đã thêm dịch vụ {branchService.Service.Name} (SL: {dto.Quantity}) vào đơn đặt sân #{result.BookingCode}.",
                            Timestamp = DateTimeHelper.GetUtcNow()
                        },
                        result
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR notification for service addition to booking {BookingId}", booking.Id);
            }

            return MapToDto(result!, includeStaffOnlyFields: true);
        }

        // Xóa dịch vụ khỏi booking, chỉ cho phép xóa khi booking đang active và invoice chưa thanh toán đủ
        public async Task<BookingDto> RemoveServiceAsync(
            Guid id, Guid serviceId,
            Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

            // LAYER A: Early validation (UX / fail-fast)
            // Validate có thể chỉnh sửa dịch vụ hay không (dựa trên data đã load)
            // NOTE: Đây chỉ là early check để fail-fast, KHÔNG phải source of truth
            // Source of truth là re-check TRONG transaction (Layer B)
            if (!CanModifyServices(booking, invoice))
                throw new AppException(400,
                    "Không thể xóa dịch vụ ở trạng thái hiện tại", ErrorCodes.BadRequest);

            var bookingService = booking.BookingServices
                .FirstOrDefault(bs => bs.ServiceId == serviceId);

            // IDEMPOTENCY: Nếu service đã bị xóa rồi → return success (không throw 404)
            // Lý do: Client có thể retry request do network issue
            // → Lần 1: success, Lần 2: không nên báo lỗi mà nên return success
            if (bookingService == null)
            {
                // Audit-grade logging cho idempotent case
                _logger.LogInformation(
                    "SERVICE_MODIFICATION | Action={Action} | BookingId={BookingId} | ServiceId={ServiceId} | " +
                    "UserId={UserId} | Result={Result}",
                    "REMOVE", id, serviceId, currentUserId, "IDEMPOTENT_SUCCESS");

                // Query lại booking và return (operation đã thành công trước đó)
                var currentBooking = await _bookingRepo.GetByIdWithDetailsAsync(id);
                return MapToDto(currentBooking!, includeStaffOnlyFields: true);
            }

            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // Khóa dòng booking trước: check-out cũng khóa dòng này, nên hóa đơn đọc dưới đây là bản mới nhất
                // và không bao giờ có dịch vụ chưa thu nằm trên hóa đơn đã PAID
                if (!await _bookingRepo.TryLockForServiceChangeAsync(booking.Id, DateTime.UtcNow))
                    throw new AppException(400,
                        "Không thể thêm/xóa dịch vụ - đơn đã kết thúc hoặc bị hủy", ErrorCodes.BadRequest);

                // LAYER B: Source of truth - Re-check TRONG transaction (CRITICAL)
                // Query fresh data từ DB và validate bằng consolidated method
                var currentStatus = await _bookingRepo.GetBookingStatusAsync(booking.Id);
                var latestInvoice = await _invoiceRepo.GetByBookingIdAsync(booking.Id);

                if (latestInvoice == null)
                    throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

                // Consolidated validation: Double protection (Status + PaymentStatus)
                EnsureBookingModifiable(currentStatus, latestInvoice.PaymentStatus);

                // Xóa service
                await _bookingRepo.RemoveServiceAsync(bookingService);

                // Tính lại service_fee từ DB (không dùng memory collection)
                // Dùng SumAsync để tối ưu performance (không load list vào memory)
                var remainingServiceFee = await _bookingRepo.CalculateServiceFeeAsync(booking.Id);

                // Cập nhật invoice (dùng latestInvoice từ DB, không dùng invoice từ memory)
                latestInvoice.ServiceFee = remainingServiceFee;
                latestInvoice.FinalTotal = latestInvoice.CourtFee
                                   - latestInvoice.LoyaltyDiscountAmount
                                   - latestInvoice.PromotionDiscountAmount
                                   + remainingServiceFee;
                latestInvoice.UpdatedAt = DateTime.UtcNow;
                await RecalculatePaymentStatusAsync(latestInvoice);
                await _invoiceRepo.UpdateAsync(latestInvoice);

                // Commit transaction
                transaction.Complete();

                // Audit-grade logging: Structured log với tất cả context quan trọng
                _logger.LogInformation(
                    "SERVICE_MODIFICATION | Action={Action} | BookingId={BookingId} | ServiceId={ServiceId} | " +
                    "ServiceName={ServiceName} | Quantity={Quantity} | UnitPrice={UnitPrice} | " +
                    "UserId={UserId} | BookingStatus={BookingStatus} | PaymentStatus={PaymentStatus} | " +
                    "OldTotal={OldTotal} | NewTotal={NewTotal}",
                    "REMOVE", booking.Id, bookingService.ServiceId, bookingService.ServiceName,
                    bookingService.Quantity, bookingService.UnitPrice,
                    currentUserId, currentStatus, latestInvoice.PaymentStatus,
                    invoice.FinalTotal, latestInvoice.FinalTotal);
            }

            // Query lại booking với details để trả về
            var result = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);

            // Broadcast SignalR notification - BookingUpdated
            try
            {
                if (result != null)
                {
                    var customerName = result.Customer?.FullName ?? result.GuestName ?? "Khách";
                    await _broadcast.BroadcastBookingEventAsync(
                        SignalREvents.BookingUpdated,
                        new BookingNotificationDto
                        {
                            BookingId = result.Id,
                            CustomerId = result.CustomerId ?? Guid.Empty,
                            CustomerName = customerName,
                            BranchId = result.BranchId,
                            BranchName = result.Branch?.Name ?? "",
                            CourtIds = result.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                            Status = result.Status.ToString(),
                            Message = $"Đã xóa dịch vụ {bookingService.ServiceName} khỏi đơn đặt sân #{result.BookingCode}.",
                            Timestamp = DateTimeHelper.GetUtcNow()
                        },
                        result
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR notification for service removal from booking {BookingId}", booking.Id);
            }

            return MapToDto(result!, includeStaffOnlyFields: true);
        }

        /// <summary>
        /// Xác nhận hoàn tiền bởi nhân viên (staff confirm refund)
        /// Flow: Validate → Update refund status → Update payment → Update invoice → Update booking → Deduct loyalty points → Send email
        /// </summary>
        /// <param name="id">Booking ID</param>
        /// <param name="confirmedBy">Staff user ID</param>
        /// <param name="currentUserRole">Staff role (OWNER/BRANCH_MANAGER/STAFF)</param>
        public async Task ConfirmRefundAsync(
            Guid id, Guid confirmedBy, string currentUserRole)
        {
            // 1. Tìm booking với details
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            // 2. Kiểm tra quyền thao tác chi nhánh (OWNER bỏ qua, MANAGER/STAFF phải thuộc chi nhánh)
            await ValidateBranchAccessAsync(booking.BranchId, confirmedBy, currentUserRole);

            // 3. Validate booking status = CANCELLED_PENDING_REFUND
            if (booking.Status != BookingStatus.CANCELLED_PENDING_REFUND)
                throw new AppException(400,
                    "Đơn không ở trạng thái chờ hoàn tiền", ErrorCodes.BadRequest);

            // 4. Mọi khoản hoàn đang chờ của đơn (một khoản cho mỗi Payment đã thu); khách nhận một lần bằng tiền mặt tại chi nhánh
            var refunds = await _refundRepo.GetPendingByBookingIdAsync(id);
            if (refunds.Count == 0)
                throw new AppException(404, "Không tìm thấy bản ghi hoàn tiền", ErrorCodes.NotFound);
            var refundAmount = refunds.Sum(r => r.Amount);

            var now = DateTime.UtcNow;

            using (var transaction = TransactionScopeHelper.ReadCommitted())
            {
                // 5.1. Hai nhân viên cùng xác nhận: chỉ một người thắng, người kia không ghi gì
                if (await _bookingRepo.UpdateWithStatusCheckAsync(
                        booking.Id, BookingStatus.CANCELLED_REFUNDED, BookingStatus.CANCELLED_PENDING_REFUND) == 0)
                    throw new AppException(409, "Đơn đã được xử lý bởi người khác", ErrorCodes.Conflict);
                booking.Status = BookingStatus.CANCELLED_REFUNDED;
                booking.UpdatedAt = now;

                // 5.2. Refund hoàn tất, cộng dồn số đã hoàn vào Payment tương ứng
                foreach (var refund in refunds)
                {
                    refund.Status = RefundStatus.COMPLETED;
                    refund.ProcessedBy = confirmedBy;
                    refund.ProcessedAt = now;
                    await _refundRepo.UpdateAsync(refund);

                    refund.Payment.RefundedAmount += refund.Amount;
                    await _paymentRepo.UpdateAsync(refund.Payment);
                }

                // 5.3. Hóa đơn REFUNDED (chỉ đổi trạng thái)
                await _invoiceRepo.SetPaymentStatusAsync(booking.Invoice!.Id, InvoicePaymentStatus.REFUNDED, now);
                booking.Invoice.PaymentStatus = InvoicePaymentStatus.REFUNDED;

                // 5.4. Trừ điểm theo % hoàn lớn nhất (VD hoàn 50 % ⇒ trừ 50 % điểm đã cộng)
                var refundPercent = refunds.Max(r => r.RefundPercent);
                if (booking.CustomerId.HasValue && refundPercent > 0)
                    await DeductLoyaltyPointsAsync(booking, refundPercent);

                transaction.Complete();
            }

            // 6. Gửi email xác nhận hoàn tiền NGOÀI transaction
            // Lỗi email không ảnh hưởng đến việc confirm refund
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (!string.IsNullOrEmpty(email))
                    await _emailService.SendRefundConfirmedAsync(
                        email, name!, booking.Id,
                        booking.Branch.Name,
                        booking.Branch.Address,
                        booking.Branch.Phone,
                        refundAmount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send refund email for booking {Id}", booking.Id);
            }

            // 7. Gửi SignalR notification - cập nhật realtime về refund completed
            try
            {
                var notification = new BookingNotificationDto
                {
                    BookingId = booking.Id,
                    CustomerId = booking.CustomerId ?? Guid.Empty,
                    CustomerName = booking.Customer?.FullName ?? booking.GuestName ?? "Khách",
                    BranchId = booking.BranchId,
                    BranchName = booking.Branch.Name,
                    CourtIds = booking.BookingCourts.Select(bc => bc.CourtId.ToString()).ToList(),
                    Status = booking.Status.ToString(),
                    Message = $"Hoàn tiền thành công cho booking #{booking.BookingCode} - Số tiền: {refundAmount:N0} VND",
                    Timestamp = DateTimeHelper.GetUtcNow()
                };

                await _broadcast.BroadcastBookingEventAsync(
                    SignalREvents.BookingRefunded,
                    notification,
                    booking
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SignalR notification for refund confirmation {BookingId}", booking.Id);
                // Don't throw - notification failure shouldn't fail the refund
            }
        }
        /// <summary>
        /// Trả về true nếu exception là lỗi vi phạm ràng buộc loại trừ (exclusion-constraint) của PostgreSQL
        /// trên bảng booking_courts (mã lỗi 23P01 / excl_booking_courts_no_overlap).
        /// Lỗi này xảy ra trong lúc SaveChangesAsync (khi INSERT BookingCourt),
        /// CHỨ KHÔNG PHẢI lúc TransactionScope.Complete(), nên phải bắt lỗi này quanh
        /// các bước ghi xuống DB, thay vì chỉ quanh lệnh Complete().
        /// </summary>
        // Hai đơn giữ cùng sân cùng giờ: 23P01 của exclusion constraint, hoặc 40P01 khi hai giao dịch chèn đồng thời và chờ chéo nhau ở
        // bước kiểm exclusion (PostgreSQL hủy một bên). Lỗi tạm thời có thể bị execution strategy của Npgsql bọc nên duyệt cả chuỗi.
        private static bool IsOverlapConstraintViolation(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is Npgsql.PostgresException pg)
                    return pg.SqlState switch
                    {
                        "23P01" => pg.ConstraintName is "excl_booking_courts_no_overlap" or "excl_slot_locks_no_overlap",
                        "40P01" => pg.Where?.Contains("booking_courts") == true || pg.Where?.Contains("slot_locks") == true,
                        _ => false
                    };
            }
            return false;
        }

        // Kiểm tra quyền thao tác chi nhánh của user, nếu là OWNER thì bỏ qua
        private async Task ValidateBranchAccessAsync(
            Guid branchId, Guid userId, string userRole)
        {
            // OWNER và CUSTOMER không cần check user_branches
            // - OWNER: có quyền toàn hệ thống
            // - CUSTOMER: chỉ xem booking của chính mình (đã check ở controller/repo)
            if (userRole == UserRole.OWNER.ToString() || userRole == UserRole.CUSTOMER.ToString())
                return;

            // STAFF và BRANCH_MANAGER phải thuộc chi nhánh mới được thao tác
            var isInBranch = await _userBranchRepo.IsUserInBranchAsync(userId, branchId);
            if (!isInBranch)
                throw new AppException(403,
                    "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden);
        }

        /// <summary>
        /// Kiểm tra xem có thể chỉnh sửa dịch vụ (add/remove) hay không
        /// Rule: Chỉ cho phép khi khách đã đến sân (IN_PROGRESS hoặc PENDING_PAYMENT)
        /// </summary>
        /// <param name="booking">Booking entity</param>
        /// <param name="invoice">Invoice entity</param>
        /// <returns>true nếu có thể chỉnh sửa, false nếu không</returns>
        /// <remarks>
        /// Business rule (Option B - Service chỉ order tại sân):
        /// - Khách đặt online → KHÔNG cho phép add service (tránh no-show, inventory issue)
        /// - Khách đến sân → Check-in → IN_PROGRESS → Cho phép add service
        /// - Hết giờ → PENDING_PAYMENT → Vẫn cho phép add service (trước khi checkout)
        /// - Đã checkout → COMPLETED → KHÔNG cho phép (đã thanh toán xong)
        /// 
        /// Payment rule:
        /// - PaymentStatus = PAID → KHÔNG cho phép (đã thanh toán đủ, không thể chỉnh sửa)
        /// - PaymentStatus = UNPAID/PARTIALLY_PAID → Cho phép (còn thiếu tiền, có thể chỉnh sửa)
        /// </remarks>
        private bool CanModifyServices(Booking booking, Invoice invoice)
        {
            // Rule 2: Chỉ cho phép khi khách đã đến sân
            // IN_PROGRESS: Khách đang chơi → cho phép add service
            // PENDING_PAYMENT: Hết giờ, chờ checkout → vẫn cho phép add service nếu cần
            return booking.Status switch
            {
                BookingStatus.IN_PROGRESS => true,
                BookingStatus.PENDING_PAYMENT => true,
                _ => false
            };
        }

        /// <summary>
        /// LAYER B: Source of truth validation - Đảm bảo booking có thể modify TRONG transaction
        /// Consolidated validation method để reuse và test riêng
        /// </summary>
        /// <param name="status">Booking status (fresh from DB)</param>
        /// <param name="paymentStatus">Invoice payment status (fresh from DB)</param>
        /// <exception cref="AppException">Throw nếu không thể modify</exception>
        /// <remarks>
        /// Method này được gọi TRONG transaction với data fresh từ DB
        /// → Đây là source of truth, không phải CanModifyServices (Layer A)
        /// </remarks>
        private void EnsureBookingModifiable(BookingStatus status, InvoicePaymentStatus paymentStatus)
        {
            if (paymentStatus == InvoicePaymentStatus.PAID)
            {
                throw new AppException(400,
                    "Không thể thêm/xóa dịch vụ - hóa đơn đã thanh toán",
                    ErrorCodes.BadRequest);
            }

            // 🟡 PRIORITY 2: Workflow State (Booking Status Check)
            // Check workflow state - quan trọng nhưng ít hơn PaymentStatus
            // Mindset: Money state > Workflow state
            if (status == BookingStatus.COMPLETED ||
                status == BookingStatus.CANCELLED ||
                status == BookingStatus.CANCELLED_PENDING_REFUND ||
                status == BookingStatus.CANCELLED_REFUNDED ||
                status == BookingStatus.NO_SHOW)
            {
                throw new AppException(400,
                    "Không thể thêm/xóa dịch vụ - đơn đã kết thúc hoặc bị hủy",
                    ErrorCodes.BadRequest);
            }
        }

        // Tích điểm loyalty dựa trên court_fee, tạo transaction và gửi email nếu lên hạng
        private async Task EarnLoyaltyPointsAsync(Booking booking, decimal courtFee)
        {
            try
            {
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(booking.CustomerId!.Value);
                if (loyalty == null) return;

                var pointsEarned = (int)Math.Floor(courtFee / 1000);
                if (pointsEarned <= 0) return;

                var tierBefore = loyalty.TierId;

                // ✔️ Variables để lưu tier info cho email (fix bug: loyalty object không được refresh)
                Guid? upgradedTierId = null;
                string? upgradedTierName = null;

                // Wrap toàn bộ loyalty logic trong transaction để đảm bảo atomicity
                // Nếu insert transaction log fail → rollback points và tier
                using (var transaction = TransactionScopeHelper.ReadCommitted())
                {
                    // Atomic update: Cập nhật TotalPoints trực tiếp trong DB để tránh race condition
                    // Không read → modify → write (có thể bị overwrite)
                    // Mà dùng: UPDATE loyalty SET total_points = total_points + @points
                    var newTotalPoints = await _loyaltyRepo.AddPointsAtomicAsync(
                        booking.CustomerId!.Value, pointsEarned);

                    // Kiểm tra lên hạng loyalty dựa trên newTotalPoints
                    var allTiers = await _loyaltyTierRepo.GetAllLoyaltyTiersAsync();
                    var newTier = allTiers
                        .Where(t => t.MinPoints <= newTotalPoints)
                        .OrderByDescending(t => t.MinPoints)
                        .FirstOrDefault();

                    // Cập nhật tier nếu thay đổi
                    // CRITICAL: So sánh với tierBefore (giá trị cũ), KHÔNG dùng loyalty.TierId
                    // Vì loyalty.TierId có thể đã bị update bởi request khác
                    if (newTier != null && newTier.Id != tierBefore)
                    {
                        await _loyaltyRepo.UpdateTierAsync(
                            booking.CustomerId!.Value, newTier.Id);

                        // ✅ FIX: Lưu tier info để gửi email sau (vì loyalty object không được refresh)
                        upgradedTierId = newTier.Id;
                        upgradedTierName = newTier.Name;
                    }

                    // Ghi transaction — CRITICAL: Phải thành công, nếu fail → rollback all
                    await _loyaltyTransactionRepo.AddAsync(new LoyaltyTransaction
                    {
                        UserId = booking.CustomerId!.Value,
                        BookingId = booking.Id,
                        Points = pointsEarned,
                        TotalPointsAfter = newTotalPoints,
                        Type = LoyaltyTransactionType.EARN,
                        CreatedAt = DateTime.UtcNow
                    });

                    // Commit transaction
                    transaction.Complete();
                }

                // ✔️ FIX: Gửi email thông báo lên hạng (NGOÀI transaction - không ảnh hưởng nếu fail)
                // Dùng upgradedTierId thay vì so sánh loyalty.TierId (vì object không được refresh)
                if (upgradedTierId.HasValue)
                {
                    try
                    {
                        var user = await _userRepo.GetUserByIdAsync(booking.CustomerId!.Value);
                        if (user != null)
                        {
                            await _emailService.SendTierUpgradeAsync(
                                user.Email, user.FullName, upgradedTierName!);

                            _logger.LogInformation(
                                "[LOYALTY] Tier upgrade email sent to {Email} for tier {TierName}",
                                user.Email, upgradedTierName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send tier upgrade email");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to earn loyalty for booking {BookingId}", booking.Id);
            }
        }

        /// <summary>
        /// Trừ điểm loyalty khi refund được confirm (theo % refund)
        /// Logic: Tìm transaction EARN → Tính điểm cần trừ → Atomic update loyalty → Check xuống hạng → Ghi transaction DEDUCT
        /// </summary>
        /// <param name="booking">Booking đã được refund</param>
        /// <param name="refundPercent">% refund (0-100)</param>
        /// <remarks>
        /// Chỉ gọi khi booking chuyển sang CANCELLED_REFUNDED
        /// Ví dụ: User được cộng 100 điểm, refund 50% → trừ 50 điểm
        /// </remarks>
        private async Task DeductLoyaltyPointsAsync(Booking booking, decimal refundPercent)
        {
            try
            {
                // 1. Chỉ trừ điểm nếu booking có customer
                if (!booking.CustomerId.HasValue) return;

                // 2. Kiểm tra xem booking này đã được cộng điểm chưa
                // Nếu chưa cộng điểm (POSTPAID chưa checkout) → không cần trừ
                var existingTransaction = await _loyaltyTransactionRepo.GetByBookingIdAsync(booking.Id);
                if (existingTransaction == null || existingTransaction.Type != LoyaltyTransactionType.EARN)
                    return; // Chưa cộng điểm thì không cần trừ (POSTPAID case)

                // 3. Kiểm tra đã trừ điểm chưa (tránh trừ lặp nếu staff confirm refund 2 lần)
                var existingDeduct = await _loyaltyTransactionRepo.GetDeductByBookingIdAsync(booking.Id);
                if (existingDeduct != null)
                {
                    _logger.LogWarning(
                        "[LOYALTY] Booking {BookingId} already has deduction, skipping",
                        booking.Id);
                    return;
                }

                // 4. Tìm loyalty record của user
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(booking.CustomerId.Value);
                if (loyalty == null) return;

                var originalPoints = existingTransaction.Points;

                // 5. Tính điểm cần trừ theo % refund
                // Dùng Math.Floor để consistent với logic cộng điểm
                // Ví dụ: 100 điểm, refund 50% → 50.0 → Floor → 50 điểm
                // Tránh trường hợp: Round lên → trừ nhiều hơn đã cộng
                var pointsToDeduct = (int)Math.Floor(
                    originalPoints * refundPercent / 100);

                if (pointsToDeduct <= 0) return; // Không có điểm cần trừ

                var tierBefore = loyalty.TierId;

                // Wrap toàn bộ loyalty logic trong transaction để đảm bảo atomicity
                // Nếu insert transaction log fail → rollback points và tier
                using (var transaction = TransactionScopeHelper.ReadCommitted())
                {
                    // 6. Atomic update: Trừ điểm trực tiếp trong DB để tránh race condition
                    // Không read → modify → write (có thể bị overwrite)
                    // Mà dùng: UPDATE loyalty SET total_points = total_points - @points (không cho âm)
                    var newTotalPoints = await _loyaltyRepo.AddPointsAtomicAsync(
                        booking.CustomerId.Value, -pointsToDeduct);

                    // 7. Kiểm tra xuống hạng dựa trên newTotalPoints
                    var allTiers = await _loyaltyTierRepo.GetAllLoyaltyTiersAsync();
                    var newTier = allTiers
                        .Where(t => t.MinPoints <= newTotalPoints)
                        .OrderByDescending(t => t.MinPoints)
                        .FirstOrDefault();

                    // Cập nhật tier nếu thay đổi
                    // CRITICAL: So sánh với tierBefore (giá trị cũ), KHÔNG dùng loyalty.TierId
                    // Vì loyalty.TierId có thể đã bị update bởi request khác
                    if (newTier != null && newTier.Id != tierBefore)
                    {
                        await _loyaltyRepo.UpdateTierAsync(
                            booking.CustomerId.Value, newTier.Id);

                        _logger.LogInformation(
                            "[LOYALTY] User {UserId} downgraded from tier {OldTier} to {NewTier} after refund",
                            booking.CustomerId.Value, tierBefore, newTier.Id);
                    }

                    // 8. Ghi transaction trừ điểm (Points = số âm để đánh dấu DEDUCT)
                    // CRITICAL: Phải thành công, nếu fail → rollback all
                    try
                    {
                        await _loyaltyTransactionRepo.AddAsync(new LoyaltyTransaction
                        {
                            UserId = booking.CustomerId.Value,
                            BookingId = booking.Id,
                            Points = -pointsToDeduct, // Số âm để đánh dấu trừ điểm
                            TotalPointsAfter = newTotalPoints,
                            Type = LoyaltyTransactionType.DEDUCT,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    catch (DbUpdateException ex) when (IsDuplicateLoyaltyDeduction(ex))
                    {
                        // Unique index violation - duplicate deduction detected
                        // Another request already processed this deduction
                        _logger.LogWarning(
                            "[LOYALTY] Duplicate deduction detected for booking {BookingId} (caught by unique index). " +
                            "Another request already processed this deduction. Skipping.",
                            booking.Id);
                        return;
                    }

                    // 9. Commit transaction
                    transaction.Complete();
                }

                // 10. Logging để tracking (NGOÀI transaction)
                _logger.LogInformation(
                    "[LOYALTY] Deducted {Points} points ({Percent}% of {Original}) from user {UserId} for refunded booking {BookingId}. Balance: {Balance}",
                    pointsToDeduct, refundPercent, originalPoints, booking.CustomerId.Value, booking.Id, await GetUserPointsBalance(booking.CustomerId.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to deduct loyalty points for booking {BookingId}", booking.Id);
                // Không throw - loyalty points không nên block refund process
            }
        }

        private static bool IsDuplicateLoyaltyDeduction(DbUpdateException exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current.Message.Contains("ux_loyalty_deduct_booking", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // Helper method để lấy balance hiện tại (cho logging)
        private async Task<int> GetUserPointsBalance(Guid userId)
        {
            try
            {
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(userId);
                return loyalty?.TotalPoints ?? 0;
            }
            catch
            {
                return 0;
            }
        }



        // Gửi email xác nhận booking với token hủy
        private async Task SendConfirmationEmailAsync(
            Booking booking, List<(CourtSlotDto Slot, Court Court)> courts)
        {
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (string.IsNullOrEmpty(email)) return;

                // DTO đã validate các sân đều có chung thời gian (StartTime, EndTime)
                var startTime = TimeOnly.FromTimeSpan(courts.First().Slot.StartTime);

                // Giờ chơi là giờ tường VN: đổi sang UTC trước khi so với now (Kind Unspecified làm Npgsql từ chối ghi)
                var token = CancelTokenHelper.Generate(
                    DateTimeHelper.ToUtcFromVietnam(booking.BookingDate, startTime),
                    DateTimeHelper.GetUtcNow());
                var rawToken = token.RawToken;

                booking.CancelTokenHash = token.TokenHash;
                booking.CancelTokenExpiresAt = token.ExpiresAtUtc;
                await _bookingRepo.UpdateAsync(booking);

                // Lấy frontend base URL từ config
                var frontendBaseUrl = _configuration["FrontendBaseUrl"] ?? "http://localhost:3000";

                // Build email model using Factory
                var emailModel = BookingEmailFactory.Build(booking, rawToken, frontendBaseUrl);

                // Send email using new method
                await _emailService.SendBookingConfirmationAsync(emailModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email for booking {BookingId}",
                    booking.Id);
            }
        }

        // Lý do cần duyệt và tên người hủy chỉ hiện cho nhân viên; khách luôn nhận null
        private static bool IsStaffView(string userRole) => userRole != UserRole.CUSTOMER.ToString();

        // Trường cọc, hạn duyệt, lý do cần duyệt, lý do / nguồn / người hủy dùng chung cho hai hàm map
        private static BookingDto WithDepositFields(BookingDto dto, Booking b, bool includeStaffOnlyFields)
        {
            var deposit = DepositFieldsMapper.FromInvoice(b.Status, b.Invoice);
            dto.DepositRate = deposit?.Rate;
            dto.DepositAmount = deposit?.Amount;
            dto.RemainingAmount = deposit?.RemainingAmount;
            dto.DepositSource = deposit?.Source.ToString();
            dto.ApprovalExpiresAt = b.ApprovalExpiresAt;
            dto.ApprovalReasonCode = includeStaffOnlyFields ? b.ApprovalReasonCode : null;
            dto.ApprovalReasons = includeStaffOnlyFields ? b.ApprovalReasons?.ToList() : null;
            dto.CancelReason = b.CancelReason?.ToString();
            dto.CancelSource = b.CancelSource?.ToString();
            dto.CancelledByName = includeStaffOnlyFields ? b.CancelledByUser?.FullName : null;
            return dto;
        }

        // map data từ entity Booking sang DTO BookingDto, bao gồm courts, price items và services
        private static BookingDto MapToDto(Booking b, bool includeStaffOnlyFields) => WithDepositFields(new BookingDto
        {
            Id = b.Id,
            BookingCode = b.BookingCode,
            InvoiceCode = b.Invoice?.InvoiceCode,
            BranchId = b.BranchId,
            BranchName = b.Branch?.Name ?? "",
            CustomerId = b.CustomerId,
            CustomerName = !string.IsNullOrEmpty(b.Customer?.FullName) ? b.Customer.FullName : b.GuestName,
            CustomerPhone = !string.IsNullOrEmpty(b.Customer?.Phone) ? b.Customer.Phone : b.GuestPhone,
            GuestName = b.GuestName,
            GuestPhone = b.GuestPhone,
            GuestEmail = b.GuestEmail,
            BookingDate = b.BookingDate.ToDateTime(TimeOnly.MinValue),
            Status = b.Status.ToString(),
            Source = b.Source.ToString(),
            Note = b.Note,
            ExpiresAt = b.Invoice?.ExpiresAt,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt,
            CourtFee = b.Invoice?.CourtFee ?? 0,
            ServiceFee = b.Invoice?.ServiceFee ?? 0,
            LoyaltyDiscountAmount = b.Invoice?.LoyaltyDiscountAmount ?? 0,
            PromotionDiscountAmount = b.Invoice?.PromotionDiscountAmount ?? 0,
            FinalTotal = b.Invoice?.FinalTotal ?? 0,
            PaymentStatus = b.Invoice?.PaymentStatus.ToString() ?? "",
            RefundAmount = b.Invoice?.Payments?
                .SelectMany(p => p.Refunds ?? Enumerable.Empty<Refund>())
                .Sum(r => (decimal?)r.Amount),
            Courts = b.BookingCourts?.Select(bc => new BookingCourtDto
            {
                CourtId = bc.CourtId,
                CourtName = bc.Court?.Name ?? "",
                StartTime = bc.StartTime.ToTimeSpan(),
                EndTime = bc.EndTime.ToTimeSpan(),
            }).ToList() ?? [],
            Services = b.BookingServices?.Select(bs => new BookingServiceDto
            {
                Id = bs.Id,
                ServiceId = bs.ServiceId,
                ServiceName = bs.ServiceName,
                Unit = bs.Unit,
                UnitPrice = bs.UnitPrice,
                Quantity = bs.Quantity,
                Total = bs.UnitPrice * bs.Quantity
            }).ToList() ?? []
        }, b, includeStaffOnlyFields);
        private static BookingDto MapToDetailDto(Booking b, bool includeStaffOnlyFields) => WithDepositFields(new BookingDto
        {
            Id = b.Id,
            BookingCode = b.BookingCode,
            InvoiceCode = b.Invoice?.InvoiceCode,
            BranchId = b.BranchId,
            BranchName = b.Branch?.Name ?? "",
            CustomerId = b.CustomerId,
            CustomerName = !string.IsNullOrEmpty(b.Customer?.FullName) ? b.Customer.FullName : b.GuestName,
            CustomerPhone = !string.IsNullOrEmpty(b.Customer?.Phone) ? b.Customer.Phone : b.GuestPhone,
            GuestName = b.GuestName,
            GuestPhone = b.GuestPhone,
            GuestEmail = b.GuestEmail,
            BookingDate = b.BookingDate.ToDateTime(TimeOnly.MinValue),
            Status = b.Status.ToString(),
            Source = b.Source.ToString(),
            Note = b.Note,
            ExpiresAt = b.Invoice?.ExpiresAt,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt,
            CourtFee = b.Invoice?.CourtFee ?? 0,
            ServiceFee = b.Invoice?.ServiceFee ?? 0,
            LoyaltyDiscountAmount = b.Invoice?.LoyaltyDiscountAmount ?? 0,
            PromotionDiscountAmount = b.Invoice?.PromotionDiscountAmount ?? 0,
            FinalTotal = b.Invoice?.FinalTotal ?? 0,
            PaymentStatus = b.Invoice?.PaymentStatus.ToString() ?? "",
            RefundAmount = b.Invoice?.Payments?
                .SelectMany(p => p.Refunds ?? Enumerable.Empty<Refund>())
                .Sum(r => (decimal?)r.Amount),
            Courts = b.BookingCourts?.Select(bc => new BookingCourtDto
            {
                CourtId = bc.CourtId,
                CourtName = bc.Court?.Name ?? "",
                StartTime = bc.StartTime.ToTimeSpan(),
                EndTime = bc.EndTime.ToTimeSpan(),
                PriceItems = PriceSlotMerger.MergeBookingPriceItems(bc.BookingPriceItems?.Select(bpi =>
                {
                    var slotHours = bpi.TimeSlot != null
                        ? (decimal)(bpi.TimeSlot.EndTime - bpi.TimeSlot.StartTime).TotalHours
                        : 0;

                    // ĐƠN GIÁ (UnitPrice) luôn trả về theo GIỜ để đảm bảo UnitPrice * Hours = SubTotal
                    // DB unitPrice đang lưu theo "slot", nên cần quy đổi ngược lại: hourlyRate = slotPrice / slotHours
                    var hourlyUnitPrice = slotHours > 0 ? bpi.UnitPrice / slotHours : bpi.UnitPrice;

                    return new BookingPriceItemDto
                    {
                        StartTime = bpi.TimeSlot?.StartTime.ToTimeSpan() ?? default,
                        EndTime = bpi.TimeSlot?.EndTime.ToTimeSpan() ?? default,
                        UnitPrice = hourlyUnitPrice,
                        Hours = slotHours,
                        SubTotal = bpi.UnitPrice // Subtotal chính là giá của slot đó
                    };
                }).ToList() ?? [])
            }).ToList() ?? [],
            Services = b.BookingServices?.Select(bs => new BookingServiceDto
            {
                Id = bs.Id,
                ServiceId = bs.ServiceId,
                ServiceName = bs.ServiceName,
                Unit = bs.Unit,
                UnitPrice = bs.UnitPrice,
                Quantity = bs.Quantity,
                Total = bs.UnitPrice * bs.Quantity
            }).ToList() ?? []
        }, b, includeStaffOnlyFields);

        // Bỏ dấu tiếng Việt để dùng trong vnp_OrderInfo (VNPay không chấp nhận Unicode)
        // RemoveDiacritics moved to StringHelper

        // Logic chung dùng để tạo chi tiết của một Booking
        private async Task<Invoice> CreateBookingDetailsAsync(
            Booking booking,
            DateOnly bookingDate,
            List<(CourtSlotDto Slot, CourtPriceResultDto Price)> priceResults,
            Promotion? promotion,
            decimal promotionDiscountAmount,
            decimal totalCourtFee,
            decimal loyaltyDiscountAmount,
            decimal finalTotal,
            PaymentTiming paymentTiming,
            PaymentHoldWindow? holdWindow = null)
        {
            var dayType = bookingDate.DayOfWeek == DayOfWeek.Saturday ||
                          bookingDate.DayOfWeek == DayOfWeek.Sunday
                ? DayType.WEEKEND : DayType.WEEKDAY;

            var allSlots = await _timeSlotRepo.GetAllAsync();

            foreach (var (slot, priceResult) in priceResults)
            {
                var bookingCourt = await _bookingRepo.AddCourtAsync(new BookingCourt
                {
                    BookingId = booking.Id,
                    CourtId = slot.CourtId,
                    Date = bookingDate,
                    StartTime = TimeOnly.FromTimeSpan(slot.StartTime),
                    EndTime = TimeOnly.FromTimeSpan(slot.EndTime),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                var priceItems = priceResult.Breakdown
                    .Select(item =>
                    {
                        var timeSlot = allSlots.FirstOrDefault(ts =>
                            ts.StartTime.ToTimeSpan() == item.StartTime &&
                            ts.EndTime.ToTimeSpan() == item.EndTime &&
                            ts.DayType == dayType);

                        return timeSlot == null ? null : new BookingPriceItem
                        {
                            BookingCourtId = bookingCourt.Id,
                            TimeSlotId = timeSlot.Id,
                            UnitPrice = item.SubTotal,
                            CreatedAt = DateTime.UtcNow
                        };
                    })
                    .Where(x => x != null)
                    .Cast<BookingPriceItem>()
                    .ToList();

                await _bookingRepo.AddPriceItemsAsync(priceItems);
            }

            if (promotion != null)
            {
                await _bookingRepo.AddPromotionAsync(new BookingPromotion
                {
                    BookingId = booking.Id,
                    PromotionId = promotion.Id,
                    PromotionNameSnapshot = promotion.Name,
                    PromotionCodeSnapshot = promotion.Code,
                    DiscountTypeSnapshot = promotion.DiscountType,
                    DiscountValueSnapshot = promotion.DiscountValue,
                    DiscountAmount = promotionDiscountAmount,
                    CreatedAt = DateTime.UtcNow
                });

                // Only increment immediately if the customer is paying at the counter later.
                // Online prepaid orders will increment inside HandleVnPayIpnAsync when money is secured.
                if (paymentTiming == PaymentTiming.POSTPAID)
                {
                    promotion.UsedCount++;
                    await _promotionRepo.UpdateAsync(promotion);
                }
            }

            var invoiceCode = await _codeGeneratorService.GenerateInvoiceCodeAsync();
            // Đơn online truyền hạn theo PaymentHoldPolicy (7.5); đơn tại quầy giữ hằng 10 phút như trước
            var expiresAt = holdWindow?.ExpiresAt ?? DateTime.UtcNow.AddMinutes(10);

            var invoice = await _invoiceRepo.CreateAsync(new Invoice
            {
                InvoiceCode = invoiceCode,
                BookingId = booking.Id,
                CourtFee = totalCourtFee,
                ServiceFee = 0,
                LoyaltyDiscountAmount = loyaltyDiscountAmount,
                PromotionDiscountAmount = promotionDiscountAmount,
                FinalTotal = finalTotal,
                PaymentStatus = InvoicePaymentStatus.UNPAID,
                PaymentTiming = paymentTiming,
                ExpiresAt = expiresAt,
                HoldDeadline = holdWindow?.HoldDeadline,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            return invoice;
        }
        private async Task RecalculatePaymentStatusAsync(Invoice invoice)
        {
            var collected =
                await _paymentRepo.GetCollectedAmountAsync(invoice.Id);

            invoice.PaymentStatus =
                collected switch
                {
                    <= 0 => InvoicePaymentStatus.UNPAID,

                    _ when collected < invoice.FinalTotal =>
                        InvoicePaymentStatus.PARTIALLY_PAID,

                    _ =>
                        InvoicePaymentStatus.PAID
                };
        }

        /// <summary>
        /// Validates promotion with all conditions using PromotionEngineService
        /// </summary>
        private async Task<(Promotion? promotion, decimal discountAmount)> ValidateAndApplyPromotionAsync(
            Guid? promotionId,
            Guid? customerId,
            Guid branchId,
            List<(CourtSlotDto Slot, Court Court)> courtEntities,
            DateTime bookingDate,
            decimal totalAfterLoyalty)
        {
            if (!promotionId.HasValue)
                return (null, 0);

            // Khách vãng lai không được dùng promotion
            if (!customerId.HasValue)
                throw new AppException(400,
                    "Khách vãng lai không thể sử dụng khuyến mãi", ErrorCodes.BadRequest);

            // Get promotion with conditions
            var promotion = await _promotionRepo.GetByIdWithConditionsAsync(promotionId.Value);

            if (promotion == null || promotion.Status != PromotionStatus.ACTIVE)
                throw new AppException(400,
                    "Khuyến mãi không hợp lệ hoặc đã hết hạn", ErrorCodes.BadRequest);

            // Check date range (phòng trường hợp job chưa update status)
            if (DateOnly.FromDateTime(bookingDate) < promotion.StartDate ||
                DateOnly.FromDateTime(bookingDate) > promotion.EndDate)
                throw new AppException(400,
                    "Khuyến mãi không áp dụng cho ngày đặt sân này", ErrorCodes.BadRequest);

            // Mọi khuyến mãi (kể cả không có điều kiện) đều qua PromotionEngineService để kiểm UsageLimit/UsagePerUserLimit

            // Build promotion context cho condition validation
            // Use the first court for context (tất cả courts cùng branch do đã validate)
            var firstCourt = courtEntities.First().Court;

            // Lấy số booking trước đó của customer
            var previousBookingCount = await _bookingRepo.GetCompletedBookingCountAsync(customerId.Value);

            var context = new SmashCourt_BE.Models.Promotions.PromotionContext
            {
                UserId = customerId.Value,
                BranchId = branchId,
                BookingAmount = totalAfterLoyalty,
                BookingDate = bookingDate,
                // multi-court booking sẽ có nhiều slot, nhưng chắc chắn cùng thời gian vì đã validate ở trên → lấy slot đầu tiên làm đại diện
                StartTime = courtEntities.First().Slot.StartTime,
                EndTime = courtEntities.First().Slot.EndTime,
                // Sport = firstCourt.CourtType?.Name ?? "Unknown",
                PreviousBookingCount = previousBookingCount
            };

            // Validate promotion with conditions using PromotionEngineService
            var validationResult = await _promotionEngine.ValidatePromotionDirectAsync(
                promotion,
                context);

            if (!validationResult.IsValid)
                throw new AppException(400,
                    validationResult.ErrorMessage ?? "Không đáp ứng điều kiện khuyến mãi",
                    ErrorCodes.BadRequest);

            return (promotion, validationResult.DiscountAmount);
        }

        /// <summary>
        /// Tạo lỗi slot unavailable và tự lưu interest cho các sân đang thật sự bận/locked nếu khách chọn nhận thông báo.
        /// </summary>
        private async Task<AppException> CreateSlotUnavailableExceptionAsync(
            CreateOnlineBookingDto dto,
            Guid customerId,
            IReadOnlyCollection<CourtSlotDto> requestedSlots,
            string defaultMessage)
        {
            var interestRegistered = await TryRegisterSlotInterestsFromFailedOnlineBookingAsync(
                dto,
                customerId,
                requestedSlots);

            if (interestRegistered)
                return new AppException(
                    400,
                    "Khung giờ này vừa có người đặt. Hệ thống sẽ thông báo nếu slot được giải phóng.",
                    ErrorCodes.SlotUnavailableNotifyRegistered);

            return new AppException(400, defaultMessage, ErrorCodes.SlotUnavailable);
        }

        /// <summary>
        /// Tự động lưu interest cho các requested slot đang bận hoặc đang bị khóa thanh toán.
        /// </summary>
        private async Task<bool> TryRegisterSlotInterestsFromFailedOnlineBookingAsync(
            CreateOnlineBookingDto dto,
            Guid customerId,
            IReadOnlyCollection<CourtSlotDto> requestedSlots)
        {
            // ✅ TỰ ĐỘNG đăng ký SlotInterest nếu có email hợp lệ
            // Không cần kiểm tra dto.NotifyIfUnavailable - tự động giúp khách hàng nhận thông báo
            var email = await ResolveSlotInterestEmailAsync(customerId);
            if (string.IsNullOrWhiteSpace(email))
                return false;

            var date = DateOnly.FromDateTime(dto.BookingDate);
            var registeredAny = false;

            foreach (var slot in requestedSlots)
            {
                var startTime = TimeOnly.FromTimeSpan(slot.StartTime);
                var endTime = TimeOnly.FromTimeSpan(slot.EndTime);

                if (!await IsRequestedSlotUnavailableAsync(slot, date, startTime, endTime))
                    continue;

                var exists = await _slotInterestRepo.ExistsAsync(
                    slot.CourtId,
                    date,
                    startTime,
                    endTime,
                    email);

                if (exists)
                {
                    registeredAny = true;
                    continue;
                }

                try
                {
                    await _slotInterestRepo.CreateAsync(new SlotInterest
                    {
                        CourtId = slot.CourtId,
                        Date = date,
                        StartTime = startTime,
                        EndTime = endTime,
                        Email = email.Trim().ToLower(),
                        CustomerId = customerId,
                        CreatedAt = DateTimeHelper.GetUtcNow(),
                        ExpiresAt = DateTimeHelper.ToUtcFromVietnam(date, new TimeOnly(23, 59, 59))
                    });
                }
                catch (DbUpdateException ex)
                    when (ex.InnerException is Npgsql.PostgresException pgEx &&
                          pgEx.SqlState == "23505" &&
                          pgEx.ConstraintName == "ux_slot_interests_unique")
                {
                    _logger.LogInformation(
                        "[SLOT_INTEREST] Duplicate interest ignored for Email={Email}, CourtId={CourtId}, Date={Date}, Start={Start}, End={End}",
                        email, slot.CourtId, date, startTime, endTime);
                }

                registeredAny = true;
                _logger.LogInformation(
                    "[SLOT_INTEREST] Auto registered after failed online booking | Court={CourtId} | Date={Date} | Slot={Start}-{End} | Email={Email}",
                    slot.CourtId, date, startTime, endTime, email);
            }

            return registeredAny;
        }

        // Đơn online luôn có khách đăng nhập nên dùng email tài khoản; guestEmail trong DTO bị bỏ qua
        private async Task<string?> ResolveSlotInterestEmailAsync(Guid customerId)
        {
            var user = await _userRepo.GetUserByIdAsync(customerId);
            return user?.Email;
        }

        private async Task<bool> IsRequestedSlotUnavailableAsync(
            CourtSlotDto slot,
            DateOnly date,
            TimeOnly startTime,
            TimeOnly endTime)
        {
            if (await _bookingRepo.HasOverlapAsync(slot.CourtId, date, startTime, endTime))
                return true;

            var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                slot.CourtId,
                date,
                startTime,
                endTime);

            return existingLock != null;
        }
    }
}

