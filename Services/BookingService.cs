using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Factories;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

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
        private readonly ICancelPolicyRepository _cancelPolicyRepo;
        private readonly IBranchServiceRepository _branchServiceRepo;
        private readonly ICourtRepository _courtRepo;
        private readonly IUserBranchRepository _userBranchRepo;
        private readonly IUserRepository _userRepo;
        private readonly ITimeSlotRepository _timeSlotRepo;
        private readonly IVnPayService _vnPayService;
        private readonly EmailService _emailService;
        private readonly ICodeGeneratorService _codeGeneratorService;
        private readonly ISlotInterestRepository _slotInterestRepo;
        private readonly SmashCourtContext _context;
        private readonly ILogger<BookingService> _logger;
        private readonly IConfiguration _configuration;

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
            ICancelPolicyRepository cancelPolicyRepo,
            IBranchServiceRepository branchServiceRepo,
            ICourtRepository courtRepo,
            IUserBranchRepository userBranchRepo,
            IUserRepository userRepo,
            ITimeSlotRepository timeSlotRepo,
            IVnPayService vnPayService,
            EmailService emailService,
            ICodeGeneratorService codeGeneratorService,
            ISlotInterestRepository slotInterestRepo,
            SmashCourtContext context,
            ILogger<BookingService> logger,
            IConfiguration configuration)
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
            _cancelPolicyRepo = cancelPolicyRepo;
            _branchServiceRepo = branchServiceRepo;
            _courtRepo = courtRepo;
            _userBranchRepo = userBranchRepo;
            _userRepo = userRepo;
            _timeSlotRepo = timeSlotRepo;
            _vnPayService = vnPayService;
            _emailService = emailService;
            _codeGeneratorService = codeGeneratorService;
            _slotInterestRepo = slotInterestRepo;
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }
        
        // Lấy danh sách booking theo quyền + chi nhánh + filter 
        public async Task<PagedResult<BookingDto>> GetAllAsync(
            BookingListQuery query, Guid currentUserId, string currentUserRole)
        {
            var pagedResult = await _bookingRepo.GetAllAsync(
                query, currentUserRole, currentUserId);

            return new PagedResult<BookingDto>
            {
                Items = pagedResult.Items.Select(MapToDto),
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
        /// Láº¥y thá»‘ng kÃª nhanh cho dashboard booking.
        /// Delegate xuá»‘ng Repository layer Ä‘á»ƒ query database.
        /// </summary>
        /// <param name="query">Query parameters tá»« client</param>
        /// <param name="currentUserId">User ID hiá»‡n táº¡i (tá»« JWT token)</param>
        /// <param name="currentUserRole">Role hiá»‡n táº¡i (OWNER, BRANCH_MANAGER, STAFF)</param>
        /// <returns>Thá»‘ng kÃª tá»•ng quan booking hÃ´m nay</returns>
        /// <remarks>
        /// Service layer chá»‰ delegate xuá»‘ng Repository, khÃ´ng cÃ³ business logic phá»©c táº¡p.
        /// Branch scoping Ä‘Æ°á»£c xá»­ lÃ½ á»Ÿ Repository layer.
        /// </remarks>
        public async Task<BookingDashboardSummaryDto> GetDashboardSummaryAsync(
            BookingDashboardSummaryQuery query, Guid currentUserId, string currentUserRole)
        {
            return await _bookingRepo.GetDashboardSummaryAsync(query, currentUserRole, currentUserId);
        }

        /// <summary>
        /// Láº¥y dá»¯ liá»‡u heatmap booking theo thÃ¡ng.
        /// Validate input vÃ  delegate xuá»‘ng Repository layer.
        /// </summary>
        /// <param name="query">Query parameters tá»« client</param>
        /// <param name="currentUserId">User ID hiá»‡n táº¡i (tá»« JWT token)</param>
        /// <param name="currentUserRole">Role hiá»‡n táº¡i (OWNER, BRANCH_MANAGER, STAFF)</param>
        /// <returns>Danh sÃ¡ch dá»¯ liá»‡u booking theo tá»«ng ngÃ y trong thÃ¡ng</returns>
        /// <remarks>
        /// Service layer xá»­ lÃ½ default values cho Year vÃ  Month náº¿u client khÃ´ng truyá»n.
        /// Branch scoping Ä‘Æ°á»£c xá»­ lÃ½ á»Ÿ Repository layer.
        /// </remarks>
        public async Task<List<BookingCalendarHeatmapDto>> GetCalendarHeatmapAsync(
            BookingCalendarHeatmapQuery query, Guid currentUserId, string currentUserRole)
        {
            // Default Year vÃ  Month náº¿u khÃ´ng truyá»n
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
                Items = pagedResult.Items.Select(MapToDto),
                TotalItems = pagedResult.TotalItems,
                Page = pagedResult.Page,
                PageSize = pagedResult.PageSize
            };
        }

        // Láº¥y thÃ´ng tin booking theo id, cÃ³ phÃ¢n quyá»n
        public async Task<BookingDto> GetByIdAsync(
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

                return MapToDto(booking);
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

            return MapToDto(booking);
        }

        // Ä‘áº·t sÃ¢n online, cÃ³ thá»ƒ cÃ³ hoáº·c khÃ´ng cÃ³ customerId (khÃ¡ch vÃ£ng lai), nhÆ°ng náº¿u cÃ³ thÃ¬ sáº½ gáº¯n booking vá»›i tÃ i khoáº£n Ä‘Ã³
        public async Task<OnlineBookingResponse> CreateOnlineAsync(
            CreateOnlineBookingDto dto, Guid? customerId)
        {
            // 1. Validate khách vãng lai
            if (customerId == null &&
                (string.IsNullOrEmpty(dto.GuestName) ||
                 string.IsNullOrEmpty(dto.GuestPhone) ||
                 string.IsNullOrEmpty(dto.GuestEmail)))
                throw new AppException(400,
                    "Vui lòng nhập đầy đủ họ tên, SĐT và email", ErrorCodes.BadRequest);

            if (!dto.Courts.Any())
                throw new AppException(400,
                    "Vui lòng chọn ít nhất 1 sân", ErrorCodes.BadRequest);

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

                if (court.Status == CourtStatus.IN_USE)
                    throw new AppException(400,
                        $"Sân {court.Name} đang có khách chơi", ErrorCodes.BadRequest);

                // Tất cả courts phải cùng branch
                if (courtEntities.Any() &&
                    court.BranchId != courtEntities.First().Court.BranchId)
                    throw new AppException(400,
                        "Tất cả sân phải thuộc cùng 1 chi nhánh", ErrorCodes.BadRequest);

                courtEntities.Add((courtSlot, court));
            }

            var branchId = courtEntities.First().Court.BranchId;

            // 3. Check overlap + slot_lock cho tất cả courts trước transaction.
            // Nếu fail ở bước này thì lưu slot_interest sau khi đã xác định slot thật sự unavailable.
            await _slotLockRepo.DeleteExpiredByBranchAsync(branchId);

            foreach (var (slot, court) in courtEntities)
            {
                var hasOverlap = await _bookingRepo.HasOverlapAsync(
                    slot.CourtId, DateOnly.FromDateTime(dto.BookingDate),
                    TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                if (hasOverlap)
                    throw await CreateSlotUnavailableExceptionAsync(
                        dto,
                        customerId,
                        dto.Courts,
                        $"Sân {court.Name} đã được đặt trong khung giờ này");

                var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                    slot.CourtId, DateOnly.FromDateTime(dto.BookingDate),
                    TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                if (existingLock != null)
                    throw await CreateSlotUnavailableExceptionAsync(
                        dto,
                        customerId,
                        dto.Courts,
                        $"Sân {court.Name} đang trong quá trình thanh toán");
            }

            // Bắt đầu transaction sau pre-check để tránh lưu slot_interest trong transaction thất bại.
            using var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

            // 4. TÃ­nh giÃ¡ cho tá»«ng court â€” cá»™ng láº¡i
            decimal totalCourtFee = 0;
            var priceResults = new List<(CourtSlotDto Slot, CalculatePriceResultDto Price)>();

            foreach (var (slot, court) in courtEntities)
            {
                var priceResult = await _priceService.CalculateAsync(
                    branchId,
                    new CalculatePriceDto
                    {
                        CourtId = slot.CourtId,
                        BookingDate = dto.BookingDate,
                        StartTime = slot.StartTime,
                        EndTime = slot.EndTime
                    });

                priceResults.Add((slot, priceResult));
                totalCourtFee += priceResult.CourtFee;
            }

            // 5. Loyalty discount tÃ­nh trÃªn tá»•ng court fee
            decimal loyaltyDiscountAmount = 0;
            if (customerId.HasValue)
            {
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(customerId.Value);
                if (loyalty?.Tier != null)
                    loyaltyDiscountAmount = Math.Round(
                        totalCourtFee * loyalty.Tier.DiscountRate / 100, 0);
            }

            var totalAfterLoyalty = totalCourtFee - loyaltyDiscountAmount;

            // 6. Promotion discount with condition validation
            var (promotion, promotionDiscountAmount) = await ValidateAndApplyPromotionAsync(
                dto.PromotionId,
                customerId,
                branchId,
                courtEntities,
                dto.BookingDate,
                totalAfterLoyalty);

            var finalTotal = totalAfterLoyalty - promotionDiscountAmount;

            // 7. Táº¡o booking PENDING â€” 1 booking cho táº¥t cáº£ courts
            // DÃ¹ng UTC Ä‘á»ƒ Npgsql lÆ°u timestamptz Ä‘Ãºng (Kind=Utc). Frontend tá»± convert sang VN time.
            var expiresAt = DateTime.UtcNow.AddMinutes(10);
            var bookingCode = await _codeGeneratorService.GenerateBookingCodeAsync();
            var booking = new Booking
            {
                BookingCode = bookingCode,
                BranchId = branchId,
                CustomerId = customerId,
                GuestName = dto.GuestName?.Trim(),
                GuestPhone = dto.GuestPhone?.Trim(),
                GuestEmail = dto.GuestEmail?.Trim(),
                BookingDate = DateOnly.FromDateTime(dto.BookingDate),
                Status = BookingStatus.PENDING,
                Source = BookingSource.ONLINE,
                Note = dto.Note?.Trim(),
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            booking = await _bookingRepo.CreateAsync(booking);

            // 8-10. Táº¡o BookingCourt, PriceItems, Promotion, vÃ  Invoice (logic chung)
            var invoice = await CreateBookingDetailsAsync(
                booking,
                booking.BookingDate,
                priceResults,
                promotion,
                promotionDiscountAmount,
                totalCourtFee,
                loyaltyDiscountAmount,
                finalTotal,
                PaymentTiming.PREPAID);  // Online booking luÃ´n PREPAID (tráº£ trÆ°á»›c qua VNPay)

            // 11. Táº¡o SlotLock cho tá»«ng court â€” ngÄƒn double-booking trong thá»i gian thanh toÃ¡n
            // Court.Status KHÃ”NG thay Ä‘á»•i á»Ÿ bÆ°á»›c nÃ y:
            //   - SlotLock Ä‘Ã£ Ä‘á»§ Ä‘á»ƒ block slot trong 10 phÃºt (HasOverlapAsync + GetByCourtAndTimeAsync)
            //   - Court.Status chá»‰ Ä‘á»•i khi payment xÃ¡c nháº­n (PAID_ONLINE) hoáº·c check-in (IN_USE)
            //   - Scheduled job sáº½ cleanup SlotLock + reset court status náº¿u booking PENDING expire
            foreach (var (slot, _) in courtEntities)
            {
                await _slotLockRepo.CreateAsync(new SlotLock
                {
                    CourtId = slot.CourtId,
                    BookingId = booking.Id,
                    Date = DateOnly.FromDateTime(dto.BookingDate),
                    StartTime = TimeOnly.FromTimeSpan(slot.StartTime),
                    EndTime = TimeOnly.FromTimeSpan(slot.EndTime),
                    ExpiresAt = expiresAt,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 12. Payment + VNPay URL
            // transactionRef Ä‘Æ°á»£c VnPayService log nhÆ°ng khÃ´ng dÃ¹ng lÃ m vnp_TxnRef thá»±c sá»±
            // â€” library tá»± generate PaymentId riÃªng, paymentInfo.TransactionRef má»›i lÃ  giÃ¡ trá»‹ lÆ°u vÃ o DB
            var courtNames = string.Join(", ",
                courtEntities.Select(x => x.Court.Name).Distinct());
            var courtNamesAscii = StringHelper.RemoveDiacritics(courtNames);
            var paymentInfo = _vnPayService.CreatePaymentUrl(
                booking.Id.ToString(),   // chá»‰ dÃ¹ng Ä‘á»ƒ log bÃªn trong VnPayService
                finalTotal,
                $"Dat san {courtNamesAscii}");

            await _paymentRepo.CreateAsync(new Payment
            {
                InvoiceId = invoice.Id,
                Method = PaymentTxMethod.VNPAY,
                Amount = finalTotal,
                Status = PaymentTxStatus.PENDING,
                TransactionRef = paymentInfo.TransactionRef,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // 13. COMMIT TRANSACTION
            try
            {
                transaction.Complete();
            }
            catch (Exception ex)
            {
                // Báº¯t lá»—i vi pháº¡m EXCLUDE constraint (phÃ¡t hiá»‡n race condition khi Ä‘áº·t trÃ¹ng slot)
                if (ex.InnerException?.Message?.Contains("excl_booking_courts_no_overlap") == true ||
                    ex.InnerException?.Message?.Contains("exclusion constraint") == true ||
                    ex.InnerException?.Message?.Contains("conflicting key") == true)
                {
                    transaction.Dispose();
                    _context.ChangeTracker.Clear();

                    throw await CreateSlotUnavailableExceptionAsync(
                        dto,
                        customerId,
                        dto.Courts,
                        "Sân đã được đặt bởi người khác, vui lòng chọn slot khác");
                }
                throw;
            }

            return new OnlineBookingResponse
            {
                BookingId = booking.Id,
                PaymentUrl = paymentInfo.Url,
                ExpiresAt = expiresAt,
                FinalTotal = finalTotal
            };
        }

        // Äáº·t sÃ¢n trá»±c tiáº¿p táº¡i quáº§y, luÃ´n táº¡o booking á»Ÿ tráº¡ng thÃ¡i CONFIRMED
        public async Task<BookingDto> CreateWalkInAsync(
            CreateWalkInBookingDto dto, Guid createdBy)
        {
            if (!dto.Courts.Any())
                throw new AppException(400,
                    "Vui lÃ²ng chá»n Ã­t nháº¥t 1 sÃ¢n", ErrorCodes.BadRequest);

            // 1. Load + validate táº¥t cáº£ courts
            var courtEntities = new List<(CourtSlotDto Slot, Court Court)>();

            var courtIds = dto.Courts.Select(c => c.CourtId).Distinct().ToList();
            var courtsFromDb = await _courtRepo.GetByIdsAsync(courtIds);
            var courtDict = courtsFromDb.ToDictionary(c => c.Id);

            foreach (var courtSlot in dto.Courts)
            {
                if (!courtDict.TryGetValue(courtSlot.CourtId, out var court))
                    throw new AppException(404,
                        $"KhÃ´ng tÃ¬m tháº¥y sÃ¢n {courtSlot.CourtId}", ErrorCodes.NotFound);

                if (court.Status == CourtStatus.SUSPENDED)
                    throw new AppException(400,
                        $"SÃ¢n {court.Name} Ä‘ang táº¡m ngÆ°ng hoáº¡t Ä‘á»™ng", ErrorCodes.BadRequest);

                if (court.Status == CourtStatus.IN_USE)
                    throw new AppException(400,
                        $"SÃ¢n {court.Name} Ä‘ang cÃ³ khÃ¡ch chÆ¡i", ErrorCodes.BadRequest);

                if (courtEntities.Any() &&
                    court.BranchId != courtEntities.First().Court.BranchId)
                    throw new AppException(400,
                        "Táº¥t cáº£ sÃ¢n pháº£i thuá»™c cÃ¹ng 1 chi nhÃ¡nh", ErrorCodes.BadRequest);

                courtEntities.Add((courtSlot, court));
            }

            var branchId = courtEntities.First().Court.BranchId;

            var user = await _userRepo.GetUserByIdAsync(createdBy);
            if (user == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y ngÆ°á»i dÃ¹ng", ErrorCodes.NotFound);

            if (user.Role != UserRole.OWNER)
            {
                // Staff chá»‰ Ä‘Æ°á»£c Ä‘áº·t sÃ¢n táº¡i chi nhÃ¡nh mÃ¬nh
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(createdBy, branchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Báº¡n khÃ´ng cÃ³ quyá»n Ä‘áº·t sÃ¢n táº¡i chi nhÃ¡nh nÃ y", ErrorCodes.Forbidden);
            }

            Guid bookingId;

            // báº¯t Ä‘áº§u transaction scope Ä‘á»ƒ Ä‘áº£m báº£o toÃ n bá»™ quÃ¡ trÃ¬nh Ä‘áº·t sÃ¢n lÃ  atomic, trÃ¡nh trÆ°á»ng há»£p Ä‘Ã£ táº¡o booking nhÆ°ng lá»—i á»Ÿ bÆ°á»›c táº¡o slot lock hoáº·c ngÆ°á»£c láº¡i
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
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
                            $"SÃ¢n {court.Name} Ä‘Ã£ Ä‘Æ°á»£c Ä‘áº·t trong khung giá» nÃ y",
                            ErrorCodes.BadRequest);

                    var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                        slot.CourtId, DateOnly.FromDateTime(dto.BookingDate),
                        TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                    if (existingLock != null)
                    {
                        // ExpiresAt lÆ°u UTC (Kind=Utc khi Ä‘á»c tá»« DB) â†’ so sÃ¡nh vá»›i UTC
                        var remaining = (int)(existingLock.ExpiresAt - DateTime.UtcNow).TotalMinutes;
                        throw new AppException(400,
                            $"SÃ¢n {court.Name} Ä‘ang bá»‹ khÃ³a thanh toÃ¡n ({remaining} phÃºt)",
                            ErrorCodes.BadRequest);
                    }
                }

                // 3. TÃ­nh giÃ¡ cho tá»«ng court
                decimal totalCourtFee = 0;
                var priceResults = new List<(CourtSlotDto Slot, CalculatePriceResultDto Price)>();

                foreach (var (slot, court) in courtEntities)
                {
                    var priceResult = await _priceService.CalculateAsync(
                        branchId,
                        new CalculatePriceDto
                        {
                            CourtId = slot.CourtId,
                            BookingDate = dto.BookingDate,
                            StartTime = slot.StartTime,
                            EndTime = slot.EndTime
                        });

                    priceResults.Add((slot, priceResult));
                    totalCourtFee += priceResult.CourtFee;
                }

                // 4. TÃ­nh loyalty + promotion
                decimal loyaltyDiscountAmount = 0;

                if (dto.CustomerId.HasValue)
                {
                    var loyalty = await _loyaltyRepo.GetByUserIdAsync(dto.CustomerId.Value);
                    if (loyalty?.Tier != null)
                        loyaltyDiscountAmount = Math.Round(
                            totalCourtFee * loyalty.Tier.DiscountRate / 100, 0);
                }

                // TÃ­nh tá»•ng sau khi trá»« loyalty Ä‘á»ƒ nháº¥t quÃ¡n vá»›i logic online
                var totalAfterLoyalty = totalCourtFee - loyaltyDiscountAmount;

                // Promotion discount with condition validation
                var (promotion, promotionDiscountAmount) = await ValidateAndApplyPromotionAsync(
                    dto.PromotionId,
                    dto.CustomerId,
                    branchId,
                    courtEntities,
                    dto.BookingDate,
                    totalAfterLoyalty);

                var finalTotal = totalAfterLoyalty - promotionDiscountAmount;

                // 5. XÃ¡c Ä‘á»‹nh PaymentTiming dá»±a trÃªn PayNow
                var paymentTiming = dto.PayNow ? PaymentTiming.PREPAID : PaymentTiming.POSTPAID;
                var paymentStatus = dto.PayNow ? InvoicePaymentStatus.PAID : InvoicePaymentStatus.UNPAID;

                // 6. Táº¡o booking CONFIRMED
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

                // 7-9. Táº¡o BookingCourt, PriceItems, Promotion, vÃ  Invoice (logic chung)
                var invoice = await CreateBookingDetailsAsync(
                    booking,
                    booking.BookingDate,
                    priceResults,
                    promotion,
                    promotionDiscountAmount,
                    totalCourtFee,
                    loyaltyDiscountAmount,
                    finalTotal,
                    paymentTiming);  // Walk-in: PREPAID náº¿u PayNow=true, POSTPAID náº¿u PayNow=false

                // 10. Náº¿u PayNow=true (PREPAID), cáº­p nháº­t PaymentStatus, táº¡o Payment record, vÃ  tÄƒng promotion usage
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

                    // ðŸŽ¯ TÄƒng usage count cá»§a promotion (náº¿u cÃ³)
                    // Walk-in booking vá»›i thanh toÃ¡n ngay táº¡i quáº§y
                    if (promotion != null)
                    {
                        await _promotionEngine.IncrementUsageCountAsync(promotion.Id);
                        _logger.LogInformation(
                            "[PROMOTION_USAGE] Incremented usage for walk-in booking | PromotionId={PromotionId} | BookingId={BookingId}",
                            promotion.Id, booking.Id);
                    }
                }

                // 11. Court status sáº½ Ä‘Æ°á»£c update bá»Ÿi scheduled job khi Ä‘áº¿n StartTime
                // KHÃ”NG update court á»Ÿ Ä‘Ã¢y Ä‘á»ƒ cho phÃ©p overbooking

                // 12. LÆ°u bookingId Ä‘á»ƒ query sau khi transaction complete
                bookingId = booking.Id;

                // 13. COMMIT TRANSACTION
                try
                {
                    transaction.Complete();
                }
                catch (Exception ex)
                {
                    // Báº¯t lá»—i vi pháº¡m EXCLUDE constraint (phÃ¡t hiá»‡n race condition khi Ä‘áº·t trÃ¹ng slot)
                    if (ex.InnerException?.Message?.Contains("excl_booking_courts_no_overlap") == true ||
                        ex.InnerException?.Message?.Contains("exclusion constraint") == true ||
                        ex.InnerException?.Message?.Contains("conflicting key") == true)
                    {
                        _logger.LogWarning(ex, "EXCLUDE constraint violated - race condition detected for walk-in booking");

                        throw new AppException(400,
                            "SÃ¢n Ä‘Ã£ Ä‘Æ°á»£c Ä‘áº·t bá»Ÿi ngÆ°á»i khÃ¡c, vui lÃ²ng chá»n slot khÃ¡c",
                            ErrorCodes.BadRequest);
                    }
                    throw;
                }
            } // â† Káº¿t thÃºc transaction scope

            // 14. Query booking details NGOÃ€I transaction scope
            var result = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);

            // 15. Gá»­i email xÃ¡c nháº­n CHá»ˆ cho PREPAID booking NGOÃ€I transaction â€” lá»—i email khÃ´ng áº£nh hÆ°á»Ÿng booking
            if (dto.PayNow)  // Chá»‰ gá»­i email cho PREPAID
            {
                try
                {
                    await SendConfirmationEmailAsync(result!, courtEntities.Select(c => (c.Slot, c.Court)).ToList());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send confirmation email for booking {Id}", bookingId);
                }
            }
            // Gá»­i email cho POSTPAID náº¿u cÃ³ email Ä‘á»ƒ tracking vÃ  gá»­i link há»§y
            else if (!string.IsNullOrEmpty(result!.Customer?.Email) || !string.IsNullOrEmpty(result.GuestEmail))
            {
                try
                {
                    await SendConfirmationEmailAsync(result!, courtEntities.Select(c => (c.Slot, c.Court)).ToList());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send confirmation email for booking {Id}", bookingId);
                }
            }

            return MapToDto(result!);
        }

        // Há»§y sÃ¢n bá»Ÿi nhÃ¢n viÃªn 
        public async Task CancelByStaffAsync(
            Guid id, Guid cancelledBy, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            // kiá»ƒm tra quyá»n há»§y booking theo chi nhÃ¡nh
            await ValidateBranchAccessAsync(booking.BranchId, cancelledBy, currentUserRole);

            var cancellableStatuses = new[]
            {
                BookingStatus.PENDING,
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE
                // âŒ KHÃ”NG cho há»§y IN_PROGRESS - khÃ¡ch Ä‘ang chÆ¡i pháº£i dÃ¹ng checkout sá»›m
            };

            if (!cancellableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "KhÃ´ng thá»ƒ há»§y Ä‘Æ¡n á»Ÿ tráº¡ng thÃ¡i nÃ y", ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            var now = DateTime.UtcNow;

            // Set CANCELLED trÆ°á»›c (default)
            booking.Status = BookingStatus.CANCELLED;
            booking.CancelledBy = cancelledBy;
            booking.CancelledAt = now;
            booking.CancelSource = CancelSourceEnum.STAFF;
            booking.UpdatedAt = now;

            // cáº­p nháº­t booking_court â†’ is_active = false
            await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

            // XÃ³a slot_lock náº¿u cÃ³
            await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

            // Cáº­p nháº­t court â†’ AVAILABLE (kiá»ƒm tra guard Ä‘á»ƒ trÃ¡nh conflict)
            // bc.Court Ä‘Ã£ Ä‘Æ°á»£c load sáºµn qua GetByIdWithDetailsAsync().ThenInclude
            foreach (var bc in booking.BookingCourts)
            {
                var court = bc.Court;
                if (court != null)
                {
                    // Chá»‰ set AVAILABLE náº¿u court khÃ´ng á»Ÿ tráº¡ng thÃ¡i Ä‘áº·c biá»‡t
                    if (court.Status != CourtStatus.SUSPENDED &&
                        court.Status != CourtStatus.IN_USE &&
                        court.Status != CourtStatus.INACTIVE)
                    {
                        court.Status = CourtStatus.AVAILABLE;
                        court.UpdatedAt = now;
                        await _courtRepo.UpdateAsync(court);
                    }
                }
            }

            // Xá»­ lÃ½ refund náº¿u Ä‘Ã£ thanh toÃ¡n
            decimal refundAmount = 0;
            if (invoice?.PaymentStatus != InvoicePaymentStatus.UNPAID)
            {
                // Defensive: Check BookingCourts khÃ´ng empty
                var firstCourt = booking.BookingCourts.FirstOrDefault();
                if (firstCourt == null)
                    throw new AppException(500, "Booking khÃ´ng cÃ³ sÃ¢n nÃ o", ErrorCodes.InternalError);

                var refundPercent = await CalculateRefundPercentAsync(
                    firstCourt.StartTime, booking.BookingDate);

                var payment = invoice?.Payments?.FirstOrDefault(
                    p => p.Status == PaymentTxStatus.SUCCESS);

                // Chá»‰ táº¡o refund vÃ  set CANCELLED_PENDING_REFUND khi thá»±c sá»± cÃ³ tiá»n hoÃ n
                if (payment != null && refundPercent > 0)
                {
                    // DÃ¹ng invoice.FinalTotal thay vÃ¬ payment.Amount Ä‘á»ƒ nháº¥t quÃ¡n vá»›i GetCancelInfoAsync
                    refundAmount = Math.Round(invoice!.FinalTotal * refundPercent / 100, 0);

                    await _refundRepo.CreateAsync(new Refund
                    {
                        PaymentId = payment.Id,
                        Amount = refundAmount,
                        RefundPercent = refundPercent,
                        Status = RefundStatus.PENDING,
                        CreatedAt = now
                    });

                    // Chá»‰ set CANCELLED_PENDING_REFUND khi thá»±c sá»± cÃ³ tiá»n cáº§n hoÃ n
                    booking.Status = BookingStatus.CANCELLED_PENDING_REFUND;
                }
                // refundPercent = 0 â†’ giá»¯ CANCELLED, khÃ´ng táº¡o refund
            }

            await _bookingRepo.UpdateAsync(booking);

            // ðŸŽ¯ Giáº£m usage count cá»§a promotion (náº¿u cÃ³)
            // Khi há»§y booking, cáº§n giáº£i phÃ³ng slot promotion cho customer khÃ¡c
            if (booking.BookingPromotion != null)
            {
                await _promotionRepo.DecrementUsageCountAsync(booking.BookingPromotion.PromotionId);
                _logger.LogInformation(
                    "[PROMOTION_USAGE] Decremented usage for promotion (staff cancel) | PromotionId={PromotionId} | BookingId={BookingId}",
                    booking.BookingPromotion.PromotionId, booking.Id);
            }

            // Gá»­i email thÃ´ng bÃ¡o há»§y
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (!string.IsNullOrEmpty(email))
                    await _emailService.SendCancelConfirmationAsync(
                        email, name!, booking.Id,
                        booking.Branch.Name,
                        booking.Branch.Address,
                        booking.Branch.Phone,
                        refundAmount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancel email for booking {Id}", booking.Id);
            }

            // Notify users interested in the freed slots (after commit, outside transaction)
            await NotifySlotInterestedUsersAsync(booking);

            // TODO: Broadcast SignalR
        }

        /// <summary>
        /// Há»§y booking bá»Ÿi khÃ¡ch hÃ ng (authenticated cancel from booking history)
        /// Flow: Validate ownership â†’ Atomic status update â†’ Update courts â†’ Create refund â†’ Send email
        /// </summary>
        /// <param name="id">Booking ID</param>
        /// <param name="customerId">Customer user ID (from JWT)</param>
        public async Task CancelByCustomerAsync(Guid id, Guid customerId)
        {
            // 1. Tìm booking với details
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            // 2. Validate ownership — customer chỉ được hủy booking của chính mình
            // Guest bookings (CustomerId = null) không thể hủy qua endpoint này
            if (!booking.CustomerId.HasValue || booking.CustomerId.Value != customerId)
                throw new AppException(403,
                    "Báº¡n khÃ´ng cÃ³ quyá»n há»§y Ä‘Æ¡n nÃ y", ErrorCodes.Forbidden);

            // 3. Kiểm tra tài khoản có bị khóa không
            if (booking.Customer?.Status == UserStatus.LOCKED)
                throw new AppException(403,
                    "TÃ i khoáº£n bá»‹ khÃ³a, vui lÃ²ng liÃªn há»‡ nhÃ¢n viÃªn",
                    ErrorCodes.AccountLocked);

            // 4. IDEMPOTENCY: Nếu booking đã bị hủy rồi, trả về success (không throw error)
            // Tránh lỗi khi user click nút hủy nhiều lần
            if (booking.Status == BookingStatus.CANCELLED ||
                booking.Status == BookingStatus.CANCELLED_PENDING_REFUND ||
                booking.Status == BookingStatus.CANCELLED_REFUNDED)
            {
                return;
            }

            // 5. Kiểm tra trạng thái có thể hủy không
            // Chỉ cho phép hủy CONFIRMED (walk-in) hoặc PAID_ONLINE (online booking đã thanh toán)
            var cancellableStatuses = new[]
            {
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE
            };

            if (!cancellableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "ÄÆ¡n Ä‘áº·t sÃ¢n khÃ´ng thá»ƒ há»§y á»Ÿ tráº¡ng thÃ¡i hiá»‡n táº¡i",
                    ErrorCodes.BadRequest);

            // 6. Validate booking có courts không (safety check)
            var firstCourt = booking.BookingCourts.FirstOrDefault()
                ?? throw new AppException(500, "Booking khÃ´ng cÃ³ sÃ¢n", ErrorCodes.InternalError);

            var invoice = booking.Invoice;
            decimal refundAmount = 0;
            var now = DateTime.UtcNow;

            // 7. Transaction scope - đảm bảo tất cả DB operations là atomic
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // 7.1. Set booking status = CANCELLED (default, có thể đổi thành CANCELLED_PENDING_REFUND sau)
                booking.Status = BookingStatus.CANCELLED;
                booking.CancelledAt = now;
                booking.CancelSource = CancelSourceEnum.CUSTOMER;
                booking.UpdatedAt = now;

                // 7.2. Vô hiệu hóa cancel token nếu có (customer đã cancel qua app, không cần token nữa)
                if (!string.IsNullOrEmpty(booking.CancelTokenHash))
                {
                    booking.CancelTokenUsedAt = now;
                }

                // 7.3. Cập nhật booking_courts → is_active = false
                await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

                // 7.4. Xóa slot_lock nếu có (cleanup)
                await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

                // 7.5. Batch update court status → AVAILABLE
                var courtIds = booking.BookingCourts
                    .Where(bc => bc.Court != null)
                    .Select(bc => bc.CourtId)
                    .ToList();

                if (courtIds.Any())
                {
                    // Check busy courts cho TẤT CẢ courtIds
                    var busyIds = new HashSet<Guid>();

                    foreach (var courtId in courtIds)
                    {
                        var busyCourts = await _bookingRepo.GetActiveByCourtAndDateAsync(
                            courtId, booking.BookingDate);

                        // Lọc ra courts của booking khác (không phải booking đang cancel)
                        foreach (var bc in busyCourts.Where(bc => bc.BookingId != booking.Id))
                        {
                            busyIds.Add(bc.CourtId);
                        }
                    }

                    // Chỉ update courts không bị busy
                    var courtsToUpdate = courtIds.Where(id => !busyIds.Contains(id)).ToList();

                    if (courtsToUpdate.Any())
                    {
                        await _courtRepo.BatchUpdateStatusAsync(
                            courtsToUpdate,
                            CourtStatus.AVAILABLE,
                            now);

                        _logger.LogInformation(
                            "[CANCEL_CUSTOMER] Updated {Count} courts to AVAILABLE. Skipped {SkippedCount} busy courts.",
                            courtsToUpdate.Count, busyIds.Count);
                    }
                }

                // 7.6. Xử lý refund nếu đã thanh toán
                if (invoice?.PaymentStatus != InvoicePaymentStatus.UNPAID)
                {
                        // Tính % refund dựa trên cancel policy
                    var refundPercent = await CalculateRefundPercentAsync(
                        firstCourt.StartTime, booking.BookingDate);

                    var payment = invoice?.Payments?.FirstOrDefault(
                        p => p.Status == PaymentTxStatus.SUCCESS);

                    if (payment != null && refundPercent > 0)
                    {
                        // Tính số tiền hoàn = FinalTotal * refundPercent / 100
                        refundAmount = Math.Round(invoice!.FinalTotal * refundPercent / 100, 0);

                        // Tạo refund record với status PENDING (chờ staff confirm)
                        await _refundRepo.CreateAsync(new Refund
                        {
                            PaymentId = payment.Id,
                            Amount = refundAmount,
                            RefundPercent = refundPercent,
                            Status = RefundStatus.PENDING,
                            CreatedAt = now
                        });

                        // Đổi status thành CANCELLED_PENDING_REFUND
                        booking.Status = BookingStatus.CANCELLED_PENDING_REFUND;
                    }
                }

                // 7.7. Lưu booking với status mới
                await _bookingRepo.UpdateAsync(booking);

                // 🎯 Giảm usage count của promotion (nếu có) - trong transaction để atomic
                // Khi customer cancel booking, cần giải phóng slot promotion cho customer khác
                if (booking.BookingPromotion != null)
                {
                    await _promotionRepo.DecrementUsageCountAsync(booking.BookingPromotion.PromotionId);
                    _logger.LogInformation(
                        "[PROMOTION_USAGE] Decremented usage for promotion (customer cancel) | PromotionId={PromotionId} | BookingId={BookingId}",
                        booking.BookingPromotion.PromotionId, booking.Id);
                }

                // 7.8. Commit transaction
                transaction.Complete();
            }

            // 8. Logging để tracking
            _logger.LogInformation(
                "[CANCEL_CUSTOMER] Booking {BookingId} cancelled by customer {CustomerId}. Refund: {RefundAmount} VND",
                booking.Id, customerId, refundAmount);

            // 9. Gửi email xác nhận hủy NGOÀI transaction
            // Lỗi email không ảnh hưởng đến việc hủy booking
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (!string.IsNullOrEmpty(email))
                    await _emailService.SendCancelConfirmationAsync(
                        email, name!, booking.Id,
                        booking.Branch.Name,
                        booking.Branch.Address,
                        booking.Branch.Phone,
                        refundAmount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancel email for booking {BookingId}", booking.Id);
            }

            // Notify users interested in the freed slots (sau commit, ngoài transaction)
            await NotifySlotInterestedUsersAsync(booking);

            // TODO: Broadcast SignalR để update real-time cho staff
        }

        // Láº¥y thÃ´ng tin há»§y booking theo token (dÃ¹ng cho khÃ¡ch hÃ ng há»§y booking online)
        public async Task<CancelTokenInfoDto> GetCancelInfoAsync(string token)
        {
            var tokenHash = HashToken(token);
            var booking = await _bookingRepo.GetByCancelTokenAsync(tokenHash);

            if (booking == null)
                throw new AppException(404,
                    "Link há»§y khÃ´ng há»£p lá»‡ hoáº·c Ä‘Ã£ háº¿t háº¡n", ErrorCodes.NotFound);

            if (booking.CancelTokenUsedAt.HasValue)
                throw new AppException(400,
                    "Link há»§y Ä‘Ã£ Ä‘Æ°á»£c sá»­ dá»¥ng", ErrorCodes.BadRequest);

            if (booking.CancelTokenExpiresAt < DateTimeHelper.GetUtcNow())
                throw new AppException(400,
                    "Link há»§y Ä‘Ã£ háº¿t háº¡n", ErrorCodes.BadRequest);

            // Kiá»ƒm tra tÃ i khoáº£n cÃ³ bá»‹ khÃ³a khÃ´ng
            if (booking.CustomerId.HasValue && booking.Customer?.Status == UserStatus.LOCKED)
                throw new AppException(403,
                    "TÃ i khoáº£n bá»‹ khÃ³a, vui lÃ²ng liÃªn há»‡ nhÃ¢n viÃªn Ä‘á»ƒ Ä‘Æ°á»£c há»— trá»£",
                    ErrorCodes.AccountLocked);

            // Defensive: Check BookingCourts khÃ´ng empty
            // CÃ¡c sÃ¢n trong cÃ¹ng booking Ä‘á»u cÃ³ chung StartTime/EndTime
            // â†’ láº¥y FirstOrDefault() cho thá»i gian lÃ  Ä‘Ãºng; CourtNames liá»‡t kÃª táº¥t cáº£ sÃ¢n
            var firstCourt = booking.BookingCourts.FirstOrDefault();
            if (firstCourt == null)
                throw new AppException(500, "Booking khÃ´ng cÃ³ sÃ¢n nÃ o", ErrorCodes.InternalError);

            var refundPercent = await CalculateRefundPercentAsync(
                firstCourt.StartTime, booking.BookingDate);

            var invoice = booking.Invoice;
            var refundAmount = invoice != null
                ? Math.Round(invoice.FinalTotal * refundPercent / 100, 0)
                : 0;

            return new CancelTokenInfoDto
            {
                BookingId = booking.Id,
                BranchName = booking.Branch.Name,
                CourtNames = booking.BookingCourts
                    .Select(bc => bc.Court?.Name ?? string.Empty)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList(),
                BookingDate = booking.BookingDate.ToDateTime(TimeOnly.MinValue),
                StartTime = firstCourt.StartTime.ToTimeSpan(),
                EndTime = firstCourt.EndTime.ToTimeSpan(),
                RefundAmount = refundAmount,
                RefundPercent = refundPercent,
                Status = booking.Status.ToString()
            };
        }

        /// <summary>
        /// Há»§y booking qua cancel token (link há»§y trong email)
        /// Flow: Validate â†’ Atomic token consumption â†’ Update booking â†’ Batch update courts â†’ Create refund â†’ Send email
        /// </summary>
        /// <param name="token">Cancel token tá»« URL (plain text, chÆ°a hash)</param>
        public async Task CancelByTokenAsync(string token)
        {
            // 1. Hash token và tìm booking
            var tokenHash = HashToken(token);
            var booking = await _bookingRepo.GetByCancelTokenAsync(tokenHash);

            if (booking == null)
                throw new AppException(404,
                    "Link há»§y khÃ´ng há»£p lá»‡", ErrorCodes.NotFound);

            // 2. Kiểm tra tài khoản có bị khóa không
            if (booking.CustomerId.HasValue && booking.Customer?.Status == UserStatus.LOCKED)
                throw new AppException(403,
                    "TÃ i khoáº£n bá»‹ khÃ³a, vui lÃ²ng liÃªn há»‡ nhÃ¢n viÃªn",
                    ErrorCodes.AccountLocked);

            // 3. IDEMPOTENCY: Nếu booking đã bị hủy rồi, trả về success (không throw error)
            // Tránh lỗi khi user click link hủy nhiều lần
            if (booking.Status == BookingStatus.CANCELLED ||
                booking.Status == BookingStatus.CANCELLED_PENDING_REFUND ||
                booking.Status == BookingStatus.CANCELLED_REFUNDED)
            {
                return;
            }

            var now = DateTime.UtcNow;

            // 4. ATOMIC TOKEN CONSUMPTION - Race condition protection
            // Nếu 2 users click cùng link → chỉ 1 người thắng, người kia nhận "Link đã được sử dụng"
            // TryConsumeTokenAsync dùng UPDATE ... WHERE để đảm bảo atomic
            var tokenConsumed = await _bookingRepo.TryConsumeTokenAsync(
                booking.Id, tokenHash, now);

            if (!tokenConsumed)
            {
                throw new AppException(400,
                    "Link há»§y Ä‘Ã£ Ä‘Æ°á»£c sá»­ dá»¥ng", ErrorCodes.BadRequest);
            }

            // 5. Reload booking để đảm bảo state fresh sau khi consume token
            booking = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            // 6. Kiểm tra token đã hết hạn chưa (24h hoặc trước giờ chơi)
            if (booking.CancelTokenExpiresAt < now)
                throw new AppException(400,
                    "Link há»§y Ä‘Ã£ háº¿t háº¡n", ErrorCodes.BadRequest);

            // 7. Kiểm tra trạng thái có thể hủy không
            // Chỉ cho phép hủy CONFIRMED (walk-in) hoặc PAID_ONLINE (online booking đã thanh toán)
            var cancellableStatuses = new[]
            {
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE
            };

            if (!cancellableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "ÄÆ¡n Ä‘áº·t sÃ¢n khÃ´ng thá»ƒ há»§y á»Ÿ tráº¡ng thÃ¡i hiá»‡n táº¡i",
                    ErrorCodes.BadRequest);

            // 8. Validate booking có courts không (safety check)
            var firstCourt = booking.BookingCourts.FirstOrDefault()
                ?? throw new AppException(500, "Booking khÃ´ng cÃ³ sÃ¢n", ErrorCodes.InternalError);

            var invoice = booking.Invoice;
            decimal refundAmount = 0;

            // 9. Transaction scope - đảm bảo tất cả DB operations là atomic
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // 9.1. Set booking status = CANCELLED (default, có thể đổi thành CANCELLED_PENDING_REFUND sau)
                booking.Status = BookingStatus.CANCELLED;
                booking.CancelledAt = now;
                booking.CancelSource = CancelSourceEnum.LINK;
                // NOTE: KHÔNG set CancelTokenUsedAt ở đây - DB đã set trong TryConsumeTokenAsync
                booking.UpdatedAt = now;

                // 9.2. Cập nhật booking_courts → is_active = false
                // Đánh dấu các court slot này không còn active
                await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

                // 9.3. Xóa slot_lock nếu có (cleanup)
                await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

                // 9.4. Batch update court status → AVAILABLE
                // Tránh N+1 queries bằng cách update tất cả courts cùng lúc
                var courtIds = booking.BookingCourts
                    .Where(bc => bc.Court != null)
                    .Select(bc => bc.CourtId)
                    .ToList();

                if (courtIds.Any())
                {
                    // ✔️ FIX: Check busy courts cho TẤT CẢ courtIds (không chỉ court đầu tiên)
                    // Mỗi court có thể có booking khác nhau, cần check riêng lẻ
                    var busyIds = new HashSet<Guid>();

                    foreach (var courtId in courtIds)
                    {
                        var busyCourts = await _bookingRepo.GetActiveByCourtAndDateAsync(
                            courtId, booking.BookingDate);

                        // Lọc ra courts của booking khác (không phải booking đang cancel)
                        foreach (var bc in busyCourts.Where(bc => bc.BookingId != booking.Id))
                        {
                            busyIds.Add(bc.CourtId);
                        }
                    }

                    // Chỉ update courts không bị busy
                    var courtsToUpdate = courtIds.Where(id => !busyIds.Contains(id)).ToList();

                    if (courtsToUpdate.Any())
                    {
                        await _courtRepo.BatchUpdateStatusAsync(
                            courtsToUpdate,
                            CourtStatus.AVAILABLE,
                            now);

                        _logger.LogInformation(
                            "[CANCEL] Updated {Count} courts to AVAILABLE. Skipped {SkippedCount} busy courts.",
                            courtsToUpdate.Count, busyIds.Count);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[CANCEL] All {Count} courts are busy, no status update needed.",
                            courtIds.Count);
                    }
                }

                // 9.5. Xử lý refund nếu đã thanh toán
                if (invoice?.PaymentStatus != InvoicePaymentStatus.UNPAID)
                {
                    // Tính % refund dựa trên cancel policy
                    var refundPercent = await CalculateRefundPercentAsync(
                        firstCourt.StartTime, booking.BookingDate);

                    var payment = invoice?.Payments?.FirstOrDefault(
                        p => p.Status == PaymentTxStatus.SUCCESS);

                    if (payment != null && refundPercent > 0)
                    {
                    // Tính số tiền hoàn = FinalTotal * refundPercent / 100
                        refundAmount = Math.Round(invoice!.FinalTotal * refundPercent / 100, 0);

                    // Tạo refund record với status PENDING (chờ staff confirm)
                        await _refundRepo.CreateAsync(new Refund
                        {
                            PaymentId = payment.Id,
                            Amount = refundAmount,
                            RefundPercent = refundPercent,
                            Status = RefundStatus.PENDING,
                            CreatedAt = now
                        });

                    // Đổi status thành CANCELLED_PENDING_REFUND
                        booking.Status = BookingStatus.CANCELLED_PENDING_REFUND;
                    }
                }

                // 9.6. Lưu booking với status mới
                await _bookingRepo.UpdateAsync(booking);

                // 9.7. Commit transaction
                transaction.Complete();
            }

            // 10. Logging để tracking
            _logger.LogInformation(
                "[CANCEL] Booking {BookingId} cancelled via token. Refund: {RefundAmount} VND",
                booking.Id, refundAmount);

            // 11. Gửi email xác nhận hủy NGOÀI transaction
            // Lỗi email không ảnh hưởng đến việc hủy booking
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (!string.IsNullOrEmpty(email))
                    await _emailService.SendCancelConfirmationAsync(
                        email, name!, booking.Id,
                        booking.Branch.Name,
                        booking.Branch.Address,
                        booking.Branch.Phone,
                        refundAmount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancel email for booking {BookingId}", booking.Id);
            }

            // Notify users interested in the freed slots (sau commit, ngoài transaction)
            await NotifySlotInterestedUsersAsync(booking);

            // TODO: Broadcast SignalR để update real-time cho staff
        }

        // Check-in khÃ¡ch hÃ ng Ä‘áº¿n sÃ¢n, chá»‰ cho phÃ©p check-in khi booking Ä‘ang CONFIRMED hoáº·c PAID_ONLINE
        public async Task CheckInAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);
            var bookingCourt = booking.BookingCourts.FirstOrDefault();
            if (bookingCourt == null)
                throw new AppException(500, "KhÃ´ng tÃ¬m tháº¥y sÃ¢n vÃ  khung giá» Ä‘Ã£ Ä‘áº·t", ErrorCodes.InternalError);

            var date = booking.BookingDate;

            var startLocal = date.ToDateTime(bookingCourt.StartTime);
            var endLocal = date.ToDateTime(bookingCourt.EndTime);

            var startDateTime = TimeZoneInfo.ConvertTimeToUtc(startLocal, DateTimeHelper.VNTimezone);
            var endDateTime = TimeZoneInfo.ConvertTimeToUtc(endLocal, DateTimeHelper.VNTimezone);

            var now = DateTimeHelper.GetUtcNow();

            if (now < startDateTime.AddMinutes(-15))
                throw new AppException(400, "QuÃ¡ sá»›m Ä‘á»ƒ check-in", ErrorCodes.BadRequest);

            if (now > endDateTime)
                throw new AppException(400, "ÄÃ£ quÃ¡ thá»i gian check-in", ErrorCodes.BadRequest);

            if (booking.Status != BookingStatus.CONFIRMED &&
                booking.Status != BookingStatus.PAID_ONLINE)
                throw new AppException(400,
                    "Chá»‰ cÃ³ thá»ƒ check-in Ä‘Æ¡n Ä‘ang xÃ¡c nháº­n hoáº·c Ä‘Ã£ thanh toÃ¡n trá»±c tuyáº¿n",
                    ErrorCodes.BadRequest);

            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // Validate chuyá»ƒn tráº¡ng thÃ¡i sá»­ dá»¥ng BookingStatusTransition helper
                BookingStatusTransition.ValidateTransition(booking.Status, BookingStatus.IN_PROGRESS);

                booking.Status = BookingStatus.IN_PROGRESS;
                booking.CheckedInAt = now;
                booking.UpdatedAt = now;

                // VÃ´ hiá»‡u hÃ³a cancel token khi check-in Ä‘á»ƒ khÃ¡ch khÃ´ng thá»ƒ há»§y khi Ä‘ang chÆ¡i
                if (!string.IsNullOrEmpty(booking.CancelTokenHash))
                {
                    booking.CancelTokenUsedAt = now;
                }

                await _bookingRepo.UpdateAsync(booking);

                // Cáº­p nháº­t court â†’ IN_USE
                // bc.Court Ä‘Ã£ Ä‘Æ°á»£c load sáºµn qua GetByIdWithDetailsAsync().ThenInclude
                foreach (var bc in booking.BookingCourts)
                {
                    if (bc.Court == null) continue;
                    bc.Court.Status = CourtStatus.IN_USE;
                    bc.Court.UpdatedAt = now;
                    await _courtRepo.UpdateAsync(bc.Court);
                }

                transaction.Complete();
            }

            // TODO: Broadcast SignalR
        }

        // Checkout khÃ¡ch hÃ ng rá»i sÃ¢n, cháº¥p nháº­n IN_PROGRESS (khÃ¡ch vá» sá»›m) vÃ  PENDING_PAYMENT (háº¿t giá»)
        public async Task CheckoutAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            // Cho phÃ©p checkout tá»« IN_PROGRESS (khÃ¡ch vá» sá»›m trÆ°á»›c EndTime)
            // hoáº·c PENDING_PAYMENT (Job-02 Ä‘Ã£ set sau khi háº¿t giá»)
            var checkoutableStatuses = new[]
            {
                BookingStatus.IN_PROGRESS,
                BookingStatus.PENDING_PAYMENT
            };
            if (!checkoutableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Chá»‰ cÃ³ thá»ƒ checkout Ä‘Æ¡n Ä‘ang tiáº¿n hÃ nh hoáº·c chá» thanh toÃ¡n",
                    ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "KhÃ´ng tÃ¬m tháº¥y hÃ³a Ä‘Æ¡n", ErrorCodes.InternalError);

            var now = DateTime.UtcNow;
            var originalStatus = booking.Status;

            // Bá»c toÃ n bá»™ checkout logic trong transaction Ä‘á»ƒ Ä‘áº£m báº£o atomicity
            // TrÃ¡nh trÆ°á»ng há»£p: payment created nhÆ°ng booking status update fail
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // 1. Conditional update booking status TRÆ¯á»šC (DB-level concurrency control)
                // Chá»‰ 1 trong 2 concurrent requests sáº½ thÃ nh cÃ´ng
                var rowsAffected = await _bookingRepo.UpdateWithStatusCheckAsync(
                    booking.Id,
                    BookingStatus.COMPLETED,
                    originalStatus);

                if (rowsAffected == 0)
                    throw new AppException(409,
                        "ÄÆ¡n Ä‘Ã£ Ä‘Æ°á»£c checkout bá»Ÿi ngÆ°á»i khÃ¡c",
                        ErrorCodes.Conflict);

                // CRITICAL: ExecuteUpdateAsync khÃ´ng update entity trong memory
                // Pháº£i update thá»§ cÃ´ng Ä‘á»ƒ logic phÃ­a sau dÃ¹ng Ä‘Ãºng giÃ¡ trá»‹
                booking.Status = BookingStatus.COMPLETED;
                booking.UpdatedAt = now;

                // 1.5. Query láº¡i invoice FinalTotal tá»« DB Ä‘á»ƒ Ä‘áº£m báº£o cÃ³ service má»›i nháº¥t
                // TrÃ¡nh race condition: Staff B add service sau khi Staff A Ä‘Ã£ load invoice
                var latestInvoice = await _invoiceRepo.GetByBookingIdAsync(booking.Id);
                if (latestInvoice == null)
                    throw new AppException(500, "KhÃ´ng tÃ¬m tháº¥y hÃ³a Ä‘Æ¡n", ErrorCodes.InternalError);

                // 2. Táº¡o Payment record CASH cho pháº§n chÆ°a thu tiá»n
                // DÃ¹ng latestInvoice Ä‘á»ƒ Ä‘áº£m báº£o FinalTotal vÃ  ServiceFee lÃ  má»›i nháº¥t
                // - Invoice UNPAID (walk-in chÆ°a thu tiá»n): thu toÃ n bá»™ FinalTotal
                // - Invoice PARTIALLY_PAID (online cÃ³ service fee phÃ¡t sinh): thu pháº§n ServiceFee
                // - Invoice PAID (tráº£ trÆ°á»›c Ä‘á»§): khÃ´ng cáº§n táº¡o thÃªm
                if (latestInvoice.PaymentStatus == InvoicePaymentStatus.UNPAID)
                {
                    await _paymentRepo.CreateAsync(new Payment
                    {
                        InvoiceId = latestInvoice.Id,
                        Method = PaymentTxMethod.CASH,
                        Amount = latestInvoice.FinalTotal,  // â† DÃ¹ng latest
                        Status = PaymentTxStatus.SUCCESS,
                        PaidAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else if (latestInvoice.PaymentStatus == InvoicePaymentStatus.PARTIALLY_PAID
                         && latestInvoice.ServiceFee > 0)
                {
                    // ÄÃ£ thanh toÃ¡n sÃ¢n online (PARTIALLY_PAID), cÃ²n láº¡i service fee thu táº¡i quáº§y
                    await _paymentRepo.CreateAsync(new Payment
                    {
                        InvoiceId = latestInvoice.Id,
                        Method = PaymentTxMethod.CASH,
                        Amount = latestInvoice.ServiceFee,  // â† DÃ¹ng latest
                        Status = PaymentTxStatus.SUCCESS,
                        PaidAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }

                // 3. Cáº­p nháº­t invoice â†’ PAID
                latestInvoice.PaymentStatus = InvoicePaymentStatus.PAID;
                latestInvoice.UpdatedAt = now;
                await _invoiceRepo.UpdateAsync(latestInvoice);

                // 4. Deactivate booking courts khi COMPLETED
                await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

                // 5. Cáº­p nháº­t court â†’ AVAILABLE (batch update Ä‘á»ƒ trÃ¡nh N+1 query)
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

                // 6. TÃ­ch Ä‘iá»ƒm loyalty náº¿u cÃ³ tÃ i khoáº£n (atomic update bÃªn trong)
                // DÃ¹ng invoice.CourtFee (khÃ´ng thay Ä‘á»•i) thay vÃ¬ latestInvoice
                if (booking.CustomerId.HasValue)
                    await EarnLoyaltyPointsAsync(booking, invoice.CourtFee);

                // 7. Commit transaction
                transaction.Complete();
            }

            // TODO: Broadcast SignalR
        }

        // thÃªm dá»‹ch vá»¥ vÃ o booking, chá»‰ cho phÃ©p thÃªm khi booking Ä‘ang active vÃ  invoice chÆ°a thanh toÃ¡n Ä‘á»§
        public async Task<BookingDto> AddServiceAsync(
            Guid id, AddBookingServiceDto dto,
            Guid currentUserId, string currentUserRole)
        {
            // Validate quantity > 0
            if (dto.Quantity <= 0)
                throw new AppException(400,
                    "Sá»‘ lÆ°á»£ng pháº£i lá»›n hÆ¡n 0", ErrorCodes.BadRequest);

            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "KhÃ´ng tÃ¬m tháº¥y hÃ³a Ä‘Æ¡n", ErrorCodes.InternalError);

            // LAYER A: Early validation (UX / fail-fast)
            // Validate cÃ³ thá»ƒ chá»‰nh sá»­a dá»‹ch vá»¥ hay khÃ´ng (dá»±a trÃªn data Ä‘Ã£ load)
            // NOTE: ÄÃ¢y chá»‰ lÃ  early check Ä‘á»ƒ fail-fast, KHÃ”NG pháº£i source of truth
            // Source of truth lÃ  re-check TRONG transaction (Layer B)
            if (!CanModifyServices(booking, invoice))
                throw new AppException(400,
                    "KhÃ´ng thá»ƒ thÃªm dá»‹ch vá»¥ á»Ÿ tráº¡ng thÃ¡i hiá»‡n táº¡i", ErrorCodes.BadRequest);

            // TÃ¬m branch service
            var branchService = await _branchServiceRepo.GetByBranchServiceAsync(
                booking.BranchId, dto.ServiceId);

            if (branchService == null ||
                branchService.Status != BranchServiceStatus.ENABLED ||
                branchService.Service.Status != ServiceStatus.ACTIVE)
                throw new AppException(400,
                    "Dá»‹ch vá»¥ khÃ´ng tá»“n táº¡i hoáº·c Ä‘Ã£ bá»‹ táº¯t", ErrorCodes.BadRequest);

            // Wrap toÃ n bá»™ logic trong transaction Ä‘á»ƒ Ä‘áº£m báº£o atomicity
            // Náº¿u update invoice fail â†’ rollback service insert
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // LAYER B: Source of truth - Re-check TRONG transaction (CRITICAL)
                // Query fresh data tá»« DB vÃ  validate báº±ng consolidated method
                var currentStatus = await _bookingRepo.GetBookingStatusAsync(booking.Id);
                var latestInvoice = await _invoiceRepo.GetByBookingIdAsync(booking.Id);

                if (latestInvoice == null)
                    throw new AppException(500, "KhÃ´ng tÃ¬m tháº¥y hÃ³a Ä‘Æ¡n", ErrorCodes.InternalError);

                // Consolidated validation: Double protection (Status + PaymentStatus)
                EnsureBookingModifiable(currentStatus, latestInvoice.PaymentStatus);

                // Check duplicate service - náº¿u Ä‘Ã£ tá»“n táº¡i thÃ¬ tÄƒng quantity thay vÃ¬ táº¡o má»›i
                var existingService = booking.BookingServices
                    .FirstOrDefault(bs => bs.ServiceId == dto.ServiceId);

                if (existingService != null)
                {
                    // UX tá»‘t hÆ¡n: Merge quantity báº±ng atomic update (trÃ¡nh race condition)
                    // DÃ¹ng ExecuteUpdateAsync: UPDATE SET quantity = quantity + @delta
                    // â†’ KhÃ´ng bá»‹ lost update khi 2 staff add cÃ¹ng lÃºc
                    await _bookingRepo.UpdateServiceQuantityAtomicAsync(
                        existingService.Id, dto.Quantity);
                }
                else
                {
                    // Táº¡o service má»›i vá»›i snapshot giÃ¡ + tÃªn táº¡i thá»i Ä‘iá»ƒm thÃªm
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

                // TÃ­nh láº¡i service_fee tá»« DB (khÃ´ng dÃ¹ng memory collection)
                // DÃ¹ng SumAsync Ä‘á»ƒ tá»‘i Æ°u performance (khÃ´ng load list vÃ o memory)
                var serviceFeeTotal = await _bookingRepo.CalculateServiceFeeAsync(booking.Id);

                // Cáº­p nháº­t invoice (dÃ¹ng latestInvoice tá»« DB, khÃ´ng dÃ¹ng invoice tá»« memory)
                latestInvoice.ServiceFee = serviceFeeTotal;
                latestInvoice.FinalTotal = latestInvoice.CourtFee
                                   - latestInvoice.LoyaltyDiscountAmount
                                   - latestInvoice.PromotionDiscountAmount
                                   + serviceFeeTotal;
                latestInvoice.UpdatedAt = DateTime.UtcNow;
                await _invoiceRepo.UpdateAsync(latestInvoice);

                // Commit transaction
                transaction.Complete();

                // Audit-grade logging: Structured log vá»›i táº¥t cáº£ context quan trá»ng
                _logger.LogInformation(
                    "SERVICE_MODIFICATION | Action={Action} | BookingId={BookingId} | ServiceId={ServiceId} | " +
                    "ServiceName={ServiceName} | Quantity={Quantity} | UnitPrice={UnitPrice} | " +
                    "UserId={UserId} | BookingStatus={BookingStatus} | PaymentStatus={PaymentStatus} | " +
                    "OldTotal={OldTotal} | NewTotal={NewTotal}",
                    "ADD", booking.Id, dto.ServiceId, branchService.Service.Name, dto.Quantity, branchService.Price,
                    currentUserId, currentStatus, latestInvoice.PaymentStatus,
                    invoice.FinalTotal, latestInvoice.FinalTotal);
            }

            // Query láº¡i booking vá»›i details Ä‘á»ƒ tráº£ vá»
            var result = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);
            return MapToDto(result!);
        }

        // XÃ³a dá»‹ch vá»¥ khá»i booking, chá»‰ cho phÃ©p xÃ³a khi booking Ä‘ang active vÃ  invoice chÆ°a thanh toÃ¡n Ä‘á»§
        public async Task<BookingDto> RemoveServiceAsync(
            Guid id, Guid serviceId,
            Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "KhÃ´ng tÃ¬m tháº¥y hÃ³a Ä‘Æ¡n", ErrorCodes.InternalError);

            // LAYER A: Early validation (UX / fail-fast)
            // Validate cÃ³ thá»ƒ chá»‰nh sá»­a dá»‹ch vá»¥ hay khÃ´ng (dá»±a trÃªn data Ä‘Ã£ load)
            // NOTE: ÄÃ¢y chá»‰ lÃ  early check Ä‘á»ƒ fail-fast, KHÃ”NG pháº£i source of truth
            // Source of truth lÃ  re-check TRONG transaction (Layer B)
            if (!CanModifyServices(booking, invoice))
                throw new AppException(400,
                    "KhÃ´ng thá»ƒ xÃ³a dá»‹ch vá»¥ á»Ÿ tráº¡ng thÃ¡i hiá»‡n táº¡i", ErrorCodes.BadRequest);

            var bookingService = booking.BookingServices
                .FirstOrDefault(bs => bs.Id == serviceId);

            // IDEMPOTENCY: Náº¿u service Ä‘Ã£ bá»‹ xÃ³a rá»“i â†’ return success (khÃ´ng throw 404)
            // LÃ½ do: Client cÃ³ thá»ƒ retry request do network issue
            // â†’ Láº§n 1: success, Láº§n 2: khÃ´ng nÃªn bÃ¡o lá»—i mÃ  nÃªn return success
            if (bookingService == null)
            {
                // Audit-grade logging cho idempotent case
                _logger.LogInformation(
                    "SERVICE_MODIFICATION | Action={Action} | BookingId={BookingId} | ServiceId={ServiceId} | " +
                    "UserId={UserId} | Result={Result}",
                    "REMOVE", id, serviceId, currentUserId, "IDEMPOTENT_SUCCESS");

                // Query láº¡i booking vÃ  return (operation Ä‘Ã£ thÃ nh cÃ´ng trÆ°á»›c Ä‘Ã³)
                var currentBooking = await _bookingRepo.GetByIdWithDetailsAsync(id);
                return MapToDto(currentBooking!);
            }

            // Wrap toÃ n bá»™ logic trong transaction Ä‘á»ƒ Ä‘áº£m báº£o atomicity
            // Náº¿u update invoice fail â†’ rollback service delete
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // LAYER B: Source of truth - Re-check TRONG transaction (CRITICAL)
                // Query fresh data tá»« DB vÃ  validate báº±ng consolidated method
                var currentStatus = await _bookingRepo.GetBookingStatusAsync(booking.Id);
                var latestInvoice = await _invoiceRepo.GetByBookingIdAsync(booking.Id);

                if (latestInvoice == null)
                    throw new AppException(500, "KhÃ´ng tÃ¬m tháº¥y hÃ³a Ä‘Æ¡n", ErrorCodes.InternalError);

                // Consolidated validation: Double protection (Status + PaymentStatus)
                EnsureBookingModifiable(currentStatus, latestInvoice.PaymentStatus);

                // XÃ³a service
                await _bookingRepo.RemoveServiceAsync(bookingService);

                // TÃ­nh láº¡i service_fee tá»« DB (khÃ´ng dÃ¹ng memory collection)
                // DÃ¹ng SumAsync Ä‘á»ƒ tá»‘i Æ°u performance (khÃ´ng load list vÃ o memory)
                var remainingServiceFee = await _bookingRepo.CalculateServiceFeeAsync(booking.Id);

                // Cáº­p nháº­t invoice (dÃ¹ng latestInvoice tá»« DB, khÃ´ng dÃ¹ng invoice tá»« memory)
                latestInvoice.ServiceFee = remainingServiceFee;
                latestInvoice.FinalTotal = latestInvoice.CourtFee
                                   - latestInvoice.LoyaltyDiscountAmount
                                   - latestInvoice.PromotionDiscountAmount
                                   + remainingServiceFee;
                latestInvoice.UpdatedAt = DateTime.UtcNow;
                await _invoiceRepo.UpdateAsync(latestInvoice);

                // Commit transaction
                transaction.Complete();

                // Audit-grade logging: Structured log vá»›i táº¥t cáº£ context quan trá»ng
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

            // Query láº¡i booking vá»›i details Ä‘á»ƒ tráº£ vá»
            var result = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);
            return MapToDto(result!);
        }

        /// <summary>
        /// XÃ¡c nháº­n hoÃ n tiá»n bá»Ÿi nhÃ¢n viÃªn (staff confirm refund)
        /// Flow: Validate â†’ Update refund status â†’ Update payment â†’ Update invoice â†’ Update booking â†’ Deduct loyalty points â†’ Send email
        /// </summary>
        /// <param name="id">Booking ID</param>
        /// <param name="confirmedBy">Staff user ID</param>
        /// <param name="currentUserRole">Staff role (OWNER/BRANCH_MANAGER/STAFF)</param>
        public async Task ConfirmRefundAsync(
            Guid id, Guid confirmedBy, string currentUserRole)
        {
            // 1. TÃ¬m booking vá»›i details
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y Ä‘Æ¡n Ä‘áº·t sÃ¢n", ErrorCodes.NotFound);

            // 2. Kiá»ƒm tra quyá»n thao tÃ¡c chi nhÃ¡nh (OWNER bá» qua, MANAGER/STAFF pháº£i thuá»™c chi nhÃ¡nh)
            await ValidateBranchAccessAsync(booking.BranchId, confirmedBy, currentUserRole);

            // 3. Validate booking status = CANCELLED_PENDING_REFUND
            if (booking.Status != BookingStatus.CANCELLED_PENDING_REFUND)
                throw new AppException(400,
                    "ÄÆ¡n khÃ´ng á»Ÿ tráº¡ng thÃ¡i chá» hoÃ n tiá»n", ErrorCodes.BadRequest);

            // 4. TÃ¬m refund record
            var refund = await _refundRepo.GetByBookingIdAsync(id);
            if (refund == null)
                throw new AppException(404, "KhÃ´ng tÃ¬m tháº¥y báº£n ghi hoÃ n tiá»n", ErrorCodes.NotFound);

            var now = DateTime.UtcNow;

            // 5. Transaction scope - Ä‘áº£m báº£o táº¥t cáº£ DB operations lÃ  atomic
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // 5.1. Update refund status â†’ COMPLETED
                refund.Status = RefundStatus.COMPLETED;
                refund.ProcessedBy = confirmedBy;
                refund.ProcessedAt = now;
                await _refundRepo.UpdateAsync(refund);

                // 5.2. Update payment refunded_amount
                refund.Payment.RefundedAmount = refund.Amount;
                await _paymentRepo.UpdateAsync(refund.Payment);

                // 5.3. Update invoice payment status â†’ REFUNDED
                var invoice = booking.Invoice!;
                invoice.PaymentStatus = InvoicePaymentStatus.REFUNDED;
                invoice.UpdatedAt = now;
                await _invoiceRepo.UpdateAsync(invoice);

                // 5.4. Update booking status â†’ CANCELLED_REFUNDED
                booking.Status = BookingStatus.CANCELLED_REFUNDED;
                booking.UpdatedAt = now;
                await _bookingRepo.UpdateAsync(booking);

                // 5.5. Trá»« Ä‘iá»ƒm loyalty theo % refund (chá»‰ khi cÃ³ customer vÃ  refund > 0)
                // VÃ­ dá»¥: Refund 50% â†’ trá»« 50% Ä‘iá»ƒm Ä‘Ã£ cá»™ng
                if (booking.CustomerId.HasValue && refund.RefundPercent > 0)
                    await DeductLoyaltyPointsAsync(booking, refund.RefundPercent);

                // 5.6. Commit transaction
                transaction.Complete();
            }

            // 6. Gá»­i email xÃ¡c nháº­n hoÃ n tiá»n NGOÃ€I transaction
            // Lá»—i email khÃ´ng áº£nh hÆ°á»Ÿng Ä‘áº¿n viá»‡c confirm refund
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
                        refund.Amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send refund email for booking {Id}", booking.Id);
            }
        }

        // Kiá»ƒm tra quyá»n thao tÃ¡c chi nhÃ¡nh cá»§a user, náº¿u lÃ  OWNER thÃ¬ bá» qua
        private async Task ValidateBranchAccessAsync(
            Guid branchId, Guid userId, string userRole)
        {
            if (userRole == UserRole.OWNER.ToString()) return;

            var isInBranch = await _userBranchRepo.IsUserInBranchAsync(userId, branchId);
            if (!isInBranch)
                throw new AppException(403,
                    "Báº¡n khÃ´ng cÃ³ quyá»n thao tÃ¡c chi nhÃ¡nh nÃ y", ErrorCodes.Forbidden);
        }

        // TÃ­nh pháº§n trÄƒm hoÃ n tiá»n dá»±a trÃªn cancel policy
        private async Task<decimal> CalculateRefundPercentAsync(
            TimeOnly startTime, DateOnly bookingDate)
        {
        // Lấy thời gian hiện tại ở VN để tính số giờ còn lại trước khi bắt đầu booking
            var bookingDateTime = bookingDate.ToDateTime(startTime);
            var vnNow = DateTimeHelper.GetUtcNow();

            var hoursUntilStart = (bookingDateTime - vnNow).TotalHours;

            // ÄÃ£ qua giá» báº¯t Ä‘áº§u â†’ khÃ´ng hoÃ n tiá»n
            if (hoursUntilStart < 0)
                return 0;

            var policies = await _cancelPolicyRepo.GetAllAsync();
            var applicable = policies
                .Where(p => p.HoursBefore <= hoursUntilStart)
                .OrderByDescending(p => p.HoursBefore)
                .FirstOrDefault();

            return applicable?.RefundPercent ?? 0;
        }

        /// <summary>
        /// Kiá»ƒm tra xem cÃ³ thá»ƒ chá»‰nh sá»­a dá»‹ch vá»¥ (add/remove) hay khÃ´ng
        /// Rule: Chá»‰ cho phÃ©p khi khÃ¡ch Ä‘Ã£ Ä‘áº¿n sÃ¢n (IN_PROGRESS hoáº·c PENDING_PAYMENT)
        /// </summary>
        /// <param name="booking">Booking entity</param>
        /// <param name="invoice">Invoice entity</param>
        /// <returns>true náº¿u cÃ³ thá»ƒ chá»‰nh sá»­a, false náº¿u khÃ´ng</returns>
        /// <remarks>
        /// Business rule (Option B - Service chá»‰ order táº¡i sÃ¢n):
        /// - KhÃ¡ch Ä‘áº·t online â†’ KHÃ”NG cho phÃ©p add service (trÃ¡nh no-show, inventory issue)
        /// - KhÃ¡ch Ä‘áº¿n sÃ¢n â†’ Check-in â†’ IN_PROGRESS â†’ Cho phÃ©p add service
        /// - Háº¿t giá» â†’ PENDING_PAYMENT â†’ Váº«n cho phÃ©p add service (trÆ°á»›c khi checkout)
        /// - ÄÃ£ checkout â†’ COMPLETED â†’ KHÃ”NG cho phÃ©p (Ä‘Ã£ thanh toÃ¡n xong)
        /// 
        /// Payment rule:
        /// - PaymentStatus = PAID â†’ KHÃ”NG cho phÃ©p (Ä‘Ã£ thanh toÃ¡n Ä‘á»§, khÃ´ng thá»ƒ chá»‰nh sá»­a)
        /// - PaymentStatus = UNPAID/PARTIALLY_PAID â†’ Cho phÃ©p (cÃ²n thiáº¿u tiá»n, cÃ³ thá»ƒ chá»‰nh sá»­a)
        /// </remarks>
        private bool CanModifyServices(Booking booking, Invoice invoice)
        {
            // Rule 1: ÄÃ£ thanh toÃ¡n Ä‘á»§ â†’ KHÃ”NG cho phÃ©p chá»‰nh sá»­a
            if (invoice.PaymentStatus == InvoicePaymentStatus.PAID)
                return false;

            // Rule 2: Chá»‰ cho phÃ©p khi khÃ¡ch Ä‘Ã£ Ä‘áº¿n sÃ¢n
            // IN_PROGRESS: KhÃ¡ch Ä‘ang chÆ¡i â†’ cho phÃ©p add service
            // PENDING_PAYMENT: Háº¿t giá», chá» checkout â†’ váº«n cho phÃ©p add service náº¿u cáº§n
            return booking.Status switch
            {
                BookingStatus.IN_PROGRESS => true,
                BookingStatus.PENDING_PAYMENT => true,
                _ => false
            };
        }

        /// <summary>
        /// LAYER B: Source of truth validation - Äáº£m báº£o booking cÃ³ thá»ƒ modify TRONG transaction
        /// Consolidated validation method Ä‘á»ƒ reuse vÃ  test riÃªng
        /// </summary>
        /// <param name="status">Booking status (fresh from DB)</param>
        /// <param name="paymentStatus">Invoice payment status (fresh from DB)</param>
        /// <exception cref="AppException">Throw náº¿u khÃ´ng thá»ƒ modify</exception>
        /// <remarks>
        /// Method nÃ y Ä‘Æ°á»£c gá»i TRONG transaction vá»›i data fresh tá»« DB
        /// â†’ ÄÃ¢y lÃ  source of truth, khÃ´ng pháº£i CanModifyServices (Layer A)
        /// </remarks>
        private void EnsureBookingModifiable(BookingStatus status, InvoicePaymentStatus paymentStatus)
        {
            // 🔴 PRIORITY 1: Financial Truth (Payment Status Check)
            // CRITICAL: Check này PHẢI đi trước vì PaymentStatus là source of truth cuối cùng
            // Ngăn modify sau khi đã thu tiền - quan trọng nhất về mặt tài chính
            // Case: Status = PENDING_PAYMENT + PaymentStatus = PAID → PHẢI block (đã thu tiền rồi)
            if (paymentStatus == InvoicePaymentStatus.PAID)
            {
                throw new AppException(400,
                    "KhÃ´ng thá»ƒ thÃªm/xÃ³a dá»‹ch vá»¥ - hÃ³a Ä‘Æ¡n Ä‘Ã£ thanh toÃ¡n",
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
                    "KhÃ´ng thá»ƒ thÃªm/xÃ³a dá»‹ch vá»¥ - Ä‘Æ¡n Ä‘Ã£ káº¿t thÃºc hoáº·c bá»‹ há»§y",
                    ErrorCodes.BadRequest);
            }
        }

        // TÃ­ch Ä‘iá»ƒm loyalty dá»±a trÃªn court_fee, táº¡o transaction vÃ  gá»­i email náº¿u lÃªn háº¡ng
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
                using (var transaction = new System.Transactions.TransactionScope(
                    System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
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

                        // âœ… FIX: LÆ°u tier info Ä‘á»ƒ gá»­i email sau (vÃ¬ loyalty object khÃ´ng Ä‘Æ°á»£c refresh)
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
        /// Trá»« Ä‘iá»ƒm loyalty khi refund Ä‘Æ°á»£c confirm (theo % refund)
        /// Logic: TÃ¬m transaction EARN â†’ TÃ­nh Ä‘iá»ƒm cáº§n trá»« â†’ Atomic update loyalty â†’ Check xuá»‘ng háº¡ng â†’ Ghi transaction DEDUCT
        /// </summary>
        /// <param name="booking">Booking Ä‘Ã£ Ä‘Æ°á»£c refund</param>
        /// <param name="refundPercent">% refund (0-100)</param>
        /// <remarks>
        /// Chá»‰ gá»i khi booking chuyá»ƒn sang CANCELLED_REFUNDED
        /// VÃ­ dá»¥: User Ä‘Æ°á»£c cá»™ng 100 Ä‘iá»ƒm, refund 50% â†’ trá»« 50 Ä‘iá»ƒm
        /// </remarks>
        private async Task DeductLoyaltyPointsAsync(Booking booking, decimal refundPercent)
        {
            try
            {
                // 1. Chá»‰ trá»« Ä‘iá»ƒm náº¿u booking cÃ³ customer
                if (!booking.CustomerId.HasValue) return;

                // 2. Kiá»ƒm tra xem booking nÃ y Ä‘Ã£ Ä‘Æ°á»£c cá»™ng Ä‘iá»ƒm chÆ°a
                // Náº¿u chÆ°a cá»™ng Ä‘iá»ƒm (POSTPAID chÆ°a checkout) â†’ khÃ´ng cáº§n trá»«
                var existingTransaction = await _loyaltyTransactionRepo.GetByBookingIdAsync(booking.Id);
                if (existingTransaction == null || existingTransaction.Type != LoyaltyTransactionType.EARN)
                    return; // ChÆ°a cá»™ng Ä‘iá»ƒm thÃ¬ khÃ´ng cáº§n trá»« (POSTPAID case)

                // 3. Kiá»ƒm tra Ä‘Ã£ trá»« Ä‘iá»ƒm chÆ°a (trÃ¡nh trá»« láº·p náº¿u staff confirm refund 2 láº§n)
                var existingDeduct = await _loyaltyTransactionRepo.GetDeductByBookingIdAsync(booking.Id);
                if (existingDeduct != null)
                {
                    _logger.LogWarning(
                        "[LOYALTY] Booking {BookingId} already has deduction, skipping",
                        booking.Id);
                    return;
                }

                // 4. TÃ¬m loyalty record cá»§a user
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(booking.CustomerId.Value);
                if (loyalty == null) return;

                var originalPoints = existingTransaction.Points;

                // 5. TÃ­nh Ä‘iá»ƒm cáº§n trá»« theo % refund
                // DÃ¹ng Math.Floor Ä‘á»ƒ consistent vá»›i logic cá»™ng Ä‘iá»ƒm
                // VÃ­ dá»¥: 100 Ä‘iá»ƒm, refund 50% â†’ 50.0 â†’ Floor â†’ 50 Ä‘iá»ƒm
                // TrÃ¡nh trÆ°á»ng há»£p: Round lÃªn â†’ trá»« nhiá»u hÆ¡n Ä‘Ã£ cá»™ng
                var pointsToDeduct = (int)Math.Floor(
                    originalPoints * refundPercent / 100);

                if (pointsToDeduct <= 0) return; // KhÃ´ng cÃ³ Ä‘iá»ƒm cáº§n trá»«

                var tierBefore = loyalty.TierId;

                // Wrap toÃ n bá»™ loyalty logic trong transaction Ä‘á»ƒ Ä‘áº£m báº£o atomicity
                // Náº¿u insert transaction log fail â†’ rollback points vÃ  tier
                using (var transaction = new System.Transactions.TransactionScope(
                    System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
                {
                    // 6. Atomic update: Trá»« Ä‘iá»ƒm trá»±c tiáº¿p trong DB Ä‘á»ƒ trÃ¡nh race condition
                    // KhÃ´ng read â†’ modify â†’ write (cÃ³ thá»ƒ bá»‹ overwrite)
                    // MÃ  dÃ¹ng: UPDATE loyalty SET total_points = total_points - @points (khÃ´ng cho Ã¢m)
                    var newTotalPoints = await _loyaltyRepo.AddPointsAtomicAsync(
                        booking.CustomerId.Value, -pointsToDeduct);

                    // 7. Kiá»ƒm tra xuá»‘ng háº¡ng dá»±a trÃªn newTotalPoints
                    var allTiers = await _loyaltyTierRepo.GetAllLoyaltyTiersAsync();
                    var newTier = allTiers
                        .Where(t => t.MinPoints <= newTotalPoints)
                        .OrderByDescending(t => t.MinPoints)
                        .FirstOrDefault();

                    // Cáº­p nháº­t tier náº¿u thay Ä‘á»•i
                    // CRITICAL: So sÃ¡nh vá»›i tierBefore (giÃ¡ trá»‹ cÅ©), KHÃ”NG dÃ¹ng loyalty.TierId
                    // VÃ¬ loyalty.TierId cÃ³ thá»ƒ Ä‘Ã£ bá»‹ update bá»Ÿi request khÃ¡c
                    if (newTier != null && newTier.Id != tierBefore)
                    {
                        await _loyaltyRepo.UpdateTierAsync(
                            booking.CustomerId.Value, newTier.Id);

                        _logger.LogInformation(
                            "[LOYALTY] User {UserId} downgraded from tier {OldTier} to {NewTier} after refund",
                            booking.CustomerId.Value, tierBefore, newTier.Id);
                    }

                    // 8. Ghi transaction trá»« Ä‘iá»ƒm (Points = sá»‘ Ã¢m Ä‘á»ƒ Ä‘Ã¡nh dáº¥u DEDUCT)
                    // CRITICAL: Pháº£i thÃ nh cÃ´ng, náº¿u fail â†’ rollback all
                    try
                    {
                        await _loyaltyTransactionRepo.AddAsync(new LoyaltyTransaction
                        {
                            UserId = booking.CustomerId.Value,
                            BookingId = booking.Id,
                            Points = -pointsToDeduct, // Sá»‘ Ã¢m Ä‘á»ƒ Ä‘Ã¡nh dáº¥u trá»« Ä‘iá»ƒm
                            TotalPointsAfter = newTotalPoints,
                            Type = LoyaltyTransactionType.DEDUCT,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("ux_loyalty_deduct_booking") == true)
                    {
                        // Unique index violation - duplicate deduction detected
                        // Another request already processed this deduction
                        _logger.LogWarning(
                            "[LOYALTY] Duplicate deduction detected for booking {BookingId} (caught by unique index). " +
                            "Another request already processed this deduction. Skipping.",
                            booking.Id);
                        return; // Skip gracefully - khÃ´ng rollback vÃ¬ Ä‘Ã£ cÃ³ request khÃ¡c xá»­ lÃ½
                    }

                    // 9. Commit transaction
                    transaction.Complete();
                }

                // 10. Logging Ä‘á»ƒ tracking (NGOÃ€I transaction)
                _logger.LogInformation(
                    "[LOYALTY] Deducted {Points} points ({Percent}% of {Original}) from user {UserId} for refunded booking {BookingId}. Balance: {Balance}",
                    pointsToDeduct, refundPercent, originalPoints, booking.CustomerId.Value, booking.Id, await GetUserPointsBalance(booking.CustomerId.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to deduct loyalty points for booking {BookingId}", booking.Id);
                // KhÃ´ng throw - loyalty points khÃ´ng nÃªn block refund process
            }
        }

        // Helper method Ä‘á»ƒ láº¥y balance hiá»‡n táº¡i (cho logging)
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

        // Gá»­i email xÃ¡c nháº­n booking vá»›i token há»§y
        private async Task SendConfirmationEmailAsync(
            Booking booking, List<(CourtSlotDto Slot, Court Court)> courts)
        {
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (string.IsNullOrEmpty(email)) return;

                // Táº¡o cancel token
                var rawToken = GenerateCancelToken();
                var tokenHash = HashToken(rawToken);

                // DTO Ä‘Ã£ validate cÃ¡c sÃ¢n Ä‘á»u cÃ³ chung thá»i gian (StartTime, EndTime)
                var startTime = TimeOnly.FromTimeSpan(courts.First().Slot.StartTime);
                var endTime = TimeOnly.FromTimeSpan(courts.First().Slot.EndTime);

                // Láº¥y VN time Ä‘á»ƒ nháº¥t quÃ¡n vá»›i PaymentService.SendConfirmationWithCancelTokenAsync
                var tokenExpiry = new DateTime[] {
                    booking.BookingDate.ToDateTime(startTime),
                    DateTimeHelper.GetUtcNow().AddHours(24)
                }.Min();

                booking.CancelTokenHash = tokenHash;
                booking.CancelTokenExpiresAt = tokenExpiry;
                await _bookingRepo.UpdateAsync(booking);

                // Láº¥y frontend base URL tá»« config
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

        private static string GenerateCancelToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private static string HashToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLower();
        }

        // map data tá»« entity Booking sang DTO BookingDto, bao gá»“m courts, price items vÃ  services
        private static BookingDto MapToDto(Booking b) => new()
        {
            Id = b.Id,
            BookingCode = b.BookingCode,
            InvoiceCode = b.Invoice?.InvoiceCode,
            BranchId = b.BranchId,
            BranchName = b.Branch?.Name ?? "",
            CustomerId = b.CustomerId,
            CustomerName = b.Customer?.FullName,
            CustomerPhone = b.Customer?.Phone,
            GuestName = b.GuestName,
            GuestPhone = b.GuestPhone,
            GuestEmail = b.GuestEmail,
            BookingDate = b.BookingDate.ToDateTime(TimeOnly.MinValue),
            Status = b.Status.ToString(),
            Source = b.Source.ToString(),
            Note = b.Note,
            ExpiresAt = b.ExpiresAt,
            CreatedAt = b.CreatedAt,
            UpdatedAt = b.UpdatedAt,
            CourtFee = b.Invoice?.CourtFee ?? 0,
            ServiceFee = b.Invoice?.ServiceFee ?? 0,
            LoyaltyDiscountAmount = b.Invoice?.LoyaltyDiscountAmount ?? 0,
            PromotionDiscountAmount = b.Invoice?.PromotionDiscountAmount ?? 0,
            FinalTotal = b.Invoice?.FinalTotal ?? 0,
            PaymentStatus = b.Invoice?.PaymentStatus.ToString() ?? "",
            Courts = b.BookingCourts?.Select(bc => new BookingCourtDto
            {
                CourtId = bc.CourtId,
                CourtName = bc.Court?.Name ?? "",
                StartTime = bc.StartTime.ToTimeSpan(),
                EndTime = bc.EndTime.ToTimeSpan(),
                PriceItems = bc.BookingPriceItems?.Select(bpi => new BookingPriceItemDto
                {
                    StartTime = bpi.TimeSlot?.StartTime.ToTimeSpan() ?? default,
                    EndTime = bpi.TimeSlot?.EndTime.ToTimeSpan() ?? default,
                    UnitPrice = bpi.UnitPrice,
                    Hours = bpi.TimeSlot != null
                        ? (decimal)(bpi.TimeSlot.EndTime - bpi.TimeSlot.StartTime).TotalHours
                        : 0,
                    SubTotal = bpi.UnitPrice * (bpi.TimeSlot != null
                        ? (decimal)(bpi.TimeSlot.EndTime - bpi.TimeSlot.StartTime).TotalHours
                        : 0)
                }).ToList() ?? []
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
        };

        // Bỏ dấu tiếng Việt để dùng trong vnp_OrderInfo (VNPay không chấp nhận Unicode)
        // RemoveDiacritics moved to StringHelper

        // Logic chung dÃ¹ng Ä‘á»ƒ táº¡o chi tiáº¿t cá»§a má»™t Booking
        private async Task<Invoice> CreateBookingDetailsAsync(
            Booking booking,
            DateOnly bookingDate,
            List<(CourtSlotDto Slot, CalculatePriceResultDto Price)> priceResults,
            Promotion? promotion,
            decimal promotionDiscountAmount,
            decimal totalCourtFee,
            decimal loyaltyDiscountAmount,
            decimal finalTotal,
            PaymentTiming paymentTiming)
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
                            UnitPrice = item.UnitPrice,
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

                // Increment promotion usage count
                promotion.UsedCount++;
                await _promotionRepo.UpdateAsync(promotion);
            }

            var invoiceCode = await _codeGeneratorService.GenerateInvoiceCodeAsync();
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            return invoice;
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
                    "KhÃ¡ch vÃ£ng lai khÃ´ng thá»ƒ sá»­ dá»¥ng khuyáº¿n mÃ£i", ErrorCodes.BadRequest);

            // Get promotion with conditions
            var promotion = await _promotionRepo.GetByIdWithConditionsAsync(promotionId.Value);

            if (promotion == null || promotion.Status != PromotionStatus.ACTIVE)
                throw new AppException(400,
                    "Khuyáº¿n mÃ£i khÃ´ng há»£p lá»‡ hoáº·c Ä‘Ã£ háº¿t háº¡n", ErrorCodes.BadRequest);

            // Check date range (phòng trường hợp job chưa update status)
            if (DateOnly.FromDateTime(bookingDate) < promotion.StartDate ||
                DateOnly.FromDateTime(bookingDate) > promotion.EndDate)
                throw new AppException(400,
                    "Khuyáº¿n mÃ£i khÃ´ng Ã¡p dá»¥ng cho ngÃ y Ä‘áº·t sÃ¢n nÃ y", ErrorCodes.BadRequest);

            // If no conditions, just calculate discount
            if (promotion.Conditions == null || !promotion.Conditions.Any())
            {
                var discountAmount = PromotionHelper.CalculateDiscount(promotion, totalAfterLoyalty);
                return (promotion, discountAmount);
            }

            // Build promotion context cho condition validation
            // Use the first court for context (tất cả courts cùng branch do đã validate)
            var firstCourt = courtEntities.First().Court;

            // Lấy số booking trước đó của customer
            var previousBookingCount = await _bookingRepo.GetCompletedBookingCountAsync(customerId.Value);

            var context = new SmashCourt_BE.Models.Promotions.PromotionContext
            {
                UserId = customerId.Value,
                BranchId = branchId,
                CourtId = firstCourt.Id,
                BookingAmount = totalAfterLoyalty,
                BookingDate = bookingDate,
                Sport = firstCourt.CourtType?.Name ?? "Unknown",
                PreviousBookingCount = previousBookingCount
            };

            // Validate promotion with conditions using PromotionEngineService
            var validationResult = await _promotionEngine.ValidatePromotionDirectAsync(
                promotion,
                context);

            if (!validationResult.IsValid)
                throw new AppException(400,
                    validationResult.ErrorMessage ?? "KhÃ´ng Ä‘Ã¡p á»©ng Ä‘iá»u kiá»‡n khuyáº¿n mÃ£i",
                    ErrorCodes.BadRequest);

            return (promotion, validationResult.DiscountAmount);
        }

        /// <summary>
        /// Tạo lỗi slot unavailable và tự lưu interest cho các sân đang thật sự bận/locked nếu khách chọn nhận thông báo.
        /// </summary>
        private async Task<AppException> CreateSlotUnavailableExceptionAsync(
            CreateOnlineBookingDto dto,
            Guid? customerId,
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
            Guid? customerId,
            IReadOnlyCollection<CourtSlotDto> requestedSlots)
        {
            if (!dto.NotifyIfUnavailable)
                return false;

            var email = await ResolveSlotInterestEmailAsync(dto, customerId);
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

                await _slotInterestRepo.CreateAsync(new SlotInterest
                {
                    CourtId = slot.CourtId,
                    Date = date,
                    StartTime = startTime,
                    EndTime = endTime,
                    Email = email,
                    CustomerId = customerId,
                    CreatedAt = DateTimeHelper.GetUtcNow(),
                    ExpiresAt = DateTimeHelper.ToUtcFromVietnam(date, new TimeOnly(23, 59, 59))
                });

                registeredAny = true;
                _logger.LogInformation(
                    "[SLOT_INTEREST] Auto registered after failed online booking | Court={CourtId} | Date={Date} | Slot={Start}-{End} | Email={Email}",
                    slot.CourtId, date, startTime, endTime, email);
            }

            return registeredAny;
        }

        private async Task<string?> ResolveSlotInterestEmailAsync(CreateOnlineBookingDto dto, Guid? customerId)
        {
            var email = dto.GuestEmail?.Trim();
            if (!string.IsNullOrWhiteSpace(email) || !customerId.HasValue)
                return email;

            var user = await _userRepo.GetUserByIdAsync(customerId.Value);
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


        /// <summary>
        /// Gửi thông báo email cho tất cả người đã đăng ký interest cho các slot vừa được giải phóng.
        /// Gọi SAU KHI commit transaction cancel — đảm bảo không gửi email khi DB chưa thực sự lưu.
        /// Dùng pattern chung cho mọi nguồn hủy: Staff, Customer, Token.
        /// </summary>
        private async Task NotifySlotInterestedUsersAsync(Booking booking)
        {
            var frontendUrl = _configuration["FrontendUrl"] ?? "https://smashcourt.vn";
            var branchName = booking.Branch?.Name ?? string.Empty;

            foreach (var bc in booking.BookingCourts)
            {
                var interested = await _slotInterestRepo.GetOverlappingSlotInterestsAsync(
                    bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                if (!interested.Any()) continue;

                _logger.LogInformation(
                    "[SLOT_INTEREST] Notifying {Count} users for released slot | Court={CourtId} | Date={Date} | Slot={Start}-{End}",
                    interested.Count, bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);

                var courtName = bc.Court?.Name ?? "SÃ¢n";
                var bookingUrl = $"{frontendUrl}/booking?courtId={bc.CourtId}&date={bc.Date:yyyy-MM-dd}&start={bc.StartTime:HH:mm}&end={bc.EndTime:HH:mm}";

                foreach (var interest in interested)
                {
                    try
                    {
                        await _emailService.SendSlotAvailableNotificationAsync(
                            interest.Email,
                            courtName,
                            branchName,
                            bc.Date,
                            bc.StartTime,
                            bc.EndTime,
                            bookingUrl);
                    }
                    catch (Exception ex)
                    {
                        // Lá»—i gá»­i email khÃ´ng block viá»‡c notify ngÆ°á»i khÃ¡c
                        _logger.LogError(ex,
                            "[SLOT_INTEREST] Failed to send notification to {Email} for slot Court={CourtId}",
                            interest.Email, bc.CourtId);
                    }
                }

                // XÃ³a táº¥t cáº£ interests cá»§a slot nÃ y sau khi Ä‘Ã£ notify (one-shot)
                await _slotInterestRepo.DeleteOverlappingSlotInterestsAsync(
                    bc.CourtId, bc.Date, bc.StartTime, bc.EndTime);
            }
        }
    }
}

