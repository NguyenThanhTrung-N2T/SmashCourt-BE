using SmashCourt_BE.Common;
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

        // Lấy danh sách booking của chính khách hàng (customer)
        public async Task<PagedResult<BookingDto>> GetMyBookingsAsync(
            Guid customerId, PaginationQuery query)
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

        // Lấy thông tin booking theo id, có phân quyền
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

        // đặt sân online, có thể có hoặc không có customerId (khách vãng lai), nhưng nếu có thì sẽ gắn booking với tài khoản đó
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

            // Bắt đầu transaction scope để đảm bảo toàn bộ quá trình đặt sân là atomic, tránh trường hợp đã tạo booking nhưng lỗi ở bước tạo slot lock hoặc ngược lại
            // Bọc toàn bộ các lệnh ghi DB để đảm bảo nguyên vẹn dữ liệu
            using var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled);

            // 3. Check overlap + slot_lock cho tất cả courts — atomic
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
                    throw new AppException(400,
                        $"Sân {court.Name} đang trong quá trình thanh toán",
                        ErrorCodes.BadRequest);
            }

            // 4. Tính giá cho từng court — cộng lại
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

            // 5. Loyalty discount tính trên tổng court fee
            decimal loyaltyDiscountAmount = 0;
            if (customerId.HasValue)
            {
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(customerId.Value);
                if (loyalty?.Tier != null)
                    loyaltyDiscountAmount = Math.Round(
                        totalCourtFee * loyalty.Tier.DiscountRate / 100, 0);
            }

            var totalAfterLoyalty = totalCourtFee - loyaltyDiscountAmount;

            // 6. Promotion discount
            decimal promotionDiscountAmount = 0;
            Promotion? promotion = null;
            if (dto.PromotionId.HasValue)
            {
                // #3: Khách vãng lai không được dùng promotion — báo lỗi rõ thay vì im lặng bỏ qua
                if (!customerId.HasValue)
                    throw new AppException(400,
                        "Khách vãng lai không thể sử dụng khuyến mãi", ErrorCodes.BadRequest);

                promotion = await _promotionRepo.GetByIdAsync(dto.PromotionId.Value);

                if (promotion == null || promotion.Status != PromotionStatus.ACTIVE)
                    throw new AppException(400,
                        "Khuyến mãi không hợp lệ hoặc đã hết hạn", ErrorCodes.BadRequest);

                // #2: Check ngày áp dụng — tránh trường hợp job update status chậm
                if (DateOnly.FromDateTime(dto.BookingDate) < promotion.StartDate || 
                    DateOnly.FromDateTime(dto.BookingDate) > promotion.EndDate)
                    throw new AppException(400,
                        "Khuyến mãi không áp dụng cho ngày đặt sân này", ErrorCodes.BadRequest);

                promotionDiscountAmount = Math.Round(
                    totalAfterLoyalty * promotion.DiscountRate / 100, 0);
            }

            var finalTotal = totalAfterLoyalty - promotionDiscountAmount;

            // 7. Tạo booking PENDING — 1 booking cho tất cả courts
            // Dùng UTC để Npgsql lưu timestamptz đúng (Kind=Utc). Frontend tự convert sang VN time.
            var expiresAt = DateTime.UtcNow.AddMinutes(10);
            var booking = new Booking
            {
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

            // 8-10. Tạo BookingCourt, PriceItems, Promotion, và Invoice (logic chung)
            var invoice = await CreateBookingDetailsAsync(
                booking,
                booking.BookingDate,
                priceResults,
                promotion,
                promotionDiscountAmount,
                totalCourtFee,
                loyaltyDiscountAmount,
                finalTotal,
                PaymentTiming.PREPAID);  // Online booking luôn PREPAID (trả trước qua VNPay)

            // 11. Tạo SlotLock cho từng court — ngăn double-booking trong thời gian thanh toán
            // Court.Status KHÔNG thay đổi ở bước này:
            //   - SlotLock đã đủ để block slot trong 10 phút (HasOverlapAsync + GetByCourtAndTimeAsync)
            //   - Court.Status chỉ đổi khi payment xác nhận (PAID_ONLINE) hoặc check-in (IN_USE)
            //   - Scheduled job sẽ cleanup SlotLock + reset court status nếu booking PENDING expire
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
            // transactionRef được VnPayService log nhưng không dùng làm vnp_TxnRef thực sự
            // — library tự generate PaymentId riêng, paymentInfo.TransactionRef mới là giá trị lưu vào DB
            var courtNames = string.Join(", ",
                courtEntities.Select(x => x.Court.Name).Distinct());
            var courtNamesAscii = RemoveDiacritics(courtNames);
            var paymentInfo = _vnPayService.CreatePaymentUrl(
                booking.Id.ToString(),   // chỉ dùng để log bên trong VnPayService
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
                // Bắt lỗi vi phạm EXCLUDE constraint (phát hiện race condition khi đặt trùng slot)
                if (ex.InnerException?.Message?.Contains("excl_booking_courts_no_overlap") == true ||
                    ex.InnerException?.Message?.Contains("exclusion constraint") == true ||
                    ex.InnerException?.Message?.Contains("conflicting key") == true)
                {
                    _logger.LogWarning(ex, "EXCLUDE constraint violated - race condition detected for online booking");
                    
                    throw new AppException(400,
                        "Sân đã được đặt bởi người khác, vui lòng chọn slot khác",
                        ErrorCodes.BadRequest);
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

        // Đặt sân trực tiếp tại quầy, luôn tạo booking ở trạng thái CONFIRMED
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

                if (court.Status == CourtStatus.IN_USE)
                    throw new AppException(400,
                        $"Sân {court.Name} đang có khách chơi", ErrorCodes.BadRequest);

                if (courtEntities.Any() &&
                    court.BranchId != courtEntities.First().Court.BranchId)
                    throw new AppException(400,
                        "Tất cả sân phải thuộc cùng 1 chi nhánh", ErrorCodes.BadRequest);

                courtEntities.Add((courtSlot, court));
            }

            var branchId = courtEntities.First().Court.BranchId;

            // Staff chỉ được đặt sân tại chi nhánh mình
            var isInBranch = await _userBranchRepo.IsUserInBranchAsync(createdBy, branchId);
            if (!isInBranch)
                throw new AppException(403,
                    "Bạn không có quyền đặt sân tại chi nhánh này", ErrorCodes.Forbidden);

            Guid bookingId;

            // bắt đầu transaction scope để đảm bảo toàn bộ quá trình đặt sân là atomic, tránh trường hợp đã tạo booking nhưng lỗi ở bước tạo slot lock hoặc ngược lại
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
                            $"Sân {court.Name} đã được đặt trong khung giờ này",
                            ErrorCodes.BadRequest);

                    var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                        slot.CourtId, DateOnly.FromDateTime(dto.BookingDate), 
                        TimeOnly.FromTimeSpan(slot.StartTime), TimeOnly.FromTimeSpan(slot.EndTime));
                    if (existingLock != null)
                    {
                        // ExpiresAt lưu UTC (Kind=Utc khi đọc từ DB) → so sánh với UTC
                        var remaining = (int)(existingLock.ExpiresAt - DateTime.UtcNow).TotalMinutes;
                        throw new AppException(400,
                            $"Sân {court.Name} đang bị khóa thanh toán ({remaining} phút)",
                            ErrorCodes.BadRequest);
                    }
                }

                // 3. Tính giá cho từng court
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

                // 4. Tính loyalty + promotion
                decimal loyaltyDiscountAmount = 0;
                decimal promotionDiscountAmount = 0;
                Promotion? promotion = null;

                if (dto.CustomerId.HasValue)
                {
                    var loyalty = await _loyaltyRepo.GetByUserIdAsync(dto.CustomerId.Value);
                    if (loyalty?.Tier != null)
                        loyaltyDiscountAmount = Math.Round(
                            totalCourtFee * loyalty.Tier.DiscountRate / 100, 0);
                }

                // Tính tổng sau khi trừ loyalty để nhất quán với logic online
                var totalAfterLoyalty = totalCourtFee - loyaltyDiscountAmount;

                if (dto.CustomerId.HasValue)
                {
                    if (dto.PromotionId.HasValue)
                    {
                        promotion = await _promotionRepo.GetByIdAsync(dto.PromotionId.Value);
                        if (promotion == null || promotion.Status != PromotionStatus.ACTIVE)
                            throw new AppException(400,
                                "Khuyến mãi không hợp lệ", ErrorCodes.BadRequest);

                        // #2: Check ngày áp dụng — tránh trường hợp job update status chậm
                        if (DateOnly.FromDateTime(dto.BookingDate) < promotion.StartDate || 
                            DateOnly.FromDateTime(dto.BookingDate) > promotion.EndDate)
                            throw new AppException(400,
                                "Khuyến mãi không áp dụng cho ngày đặt sân này", ErrorCodes.BadRequest);

                        promotionDiscountAmount = Math.Round(
                            totalAfterLoyalty * promotion.DiscountRate / 100, 0);
                    }
                }

                var finalTotal = totalAfterLoyalty - promotionDiscountAmount;

                // 5. Xác định PaymentTiming dựa trên PayNow
                var paymentTiming = dto.PayNow ? PaymentTiming.PREPAID : PaymentTiming.POSTPAID;
                var paymentStatus = dto.PayNow ? InvoicePaymentStatus.PAID : InvoicePaymentStatus.UNPAID;

                // 6. Tạo booking CONFIRMED
                var booking = new Booking
                {
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

                // 7-9. Tạo BookingCourt, PriceItems, Promotion, và Invoice (logic chung)
                var invoice = await CreateBookingDetailsAsync(
                    booking,
                    booking.BookingDate,
                    priceResults,
                    promotion,
                    promotionDiscountAmount,
                    totalCourtFee,
                    loyaltyDiscountAmount,
                    finalTotal,
                    paymentTiming);  // Walk-in: PREPAID nếu PayNow=true, POSTPAID nếu PayNow=false

                // 10. Nếu PayNow=true (PREPAID), cập nhật PaymentStatus và tạo Payment record
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
                }

                // 11. Court status sẽ được update bởi scheduled job khi đến StartTime
                // KHÔNG update court ở đây để cho phép overbooking

                // 12. Lưu bookingId để query sau khi transaction complete
                bookingId = booking.Id;

                // 13. COMMIT TRANSACTION
                try
                {
                    transaction.Complete();
                }
                catch (Exception ex)
                {
                    // Bắt lỗi vi phạm EXCLUDE constraint (phát hiện race condition khi đặt trùng slot)
                    if (ex.InnerException?.Message?.Contains("excl_booking_courts_no_overlap") == true ||
                        ex.InnerException?.Message?.Contains("exclusion constraint") == true ||
                        ex.InnerException?.Message?.Contains("conflicting key") == true)
                    {
                        _logger.LogWarning(ex, "EXCLUDE constraint violated - race condition detected for walk-in booking");
                        
                        throw new AppException(400,
                            "Sân đã được đặt bởi người khác, vui lòng chọn slot khác",
                            ErrorCodes.BadRequest);
                    }
                    throw;
                }
            } // ← Kết thúc transaction scope

            // 14. Query booking details NGOÀI transaction scope
            var result = await _bookingRepo.GetByIdWithDetailsAsync(bookingId);

            // 15. Gửi email xác nhận CHỈ cho PREPAID booking NGOÀI transaction — lỗi email không ảnh hưởng booking
            if (dto.PayNow)  // Chỉ gửi email cho PREPAID
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
            // Gửi email cho POSTPAID nếu có email để tracking và gửi link hủy
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

        // Hủy sân bởi nhân viên 
        public async Task CancelByStaffAsync(
            Guid id, Guid cancelledBy, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            // kiểm tra quyền hủy booking theo chi nhánh
            await ValidateBranchAccessAsync(booking.BranchId, cancelledBy, currentUserRole);

            var cancellableStatuses = new[]
            {
                BookingStatus.PENDING,
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE
                // ❌ KHÔNG cho hủy IN_PROGRESS - khách đang chơi phải dùng checkout sớm
            };

            if (!cancellableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Không thể hủy đơn ở trạng thái này", ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            var now = DateTime.UtcNow;

            // Set CANCELLED trước (default)
            booking.Status = BookingStatus.CANCELLED;
            booking.CancelledBy = cancelledBy;
            booking.CancelledAt = now;
            booking.CancelSource = CancelSourceEnum.STAFF;
            booking.UpdatedAt = now;

            // cập nhật booking_court → is_active = false
            await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

            // Xóa slot_lock nếu có
            await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

            // Cập nhật court → AVAILABLE (kiểm tra guard để tránh conflict)
            // bc.Court đã được load sẵn qua GetByIdWithDetailsAsync().ThenInclude
            foreach (var bc in booking.BookingCourts)
            {
                var court = bc.Court;
                if (court != null)
                {
                    // Chỉ set AVAILABLE nếu court không ở trạng thái đặc biệt
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

            // Xử lý refund nếu đã thanh toán
            decimal refundAmount = 0;
            if (invoice?.PaymentStatus != InvoicePaymentStatus.UNPAID)
            {
                // Defensive: Check BookingCourts không empty
                var firstCourt = booking.BookingCourts.FirstOrDefault();
                if (firstCourt == null)
                    throw new AppException(500, "Booking không có sân nào", ErrorCodes.InternalError);

                var refundPercent = await CalculateRefundPercentAsync(
                    firstCourt.StartTime, booking.BookingDate);

                var payment = invoice?.Payments?.FirstOrDefault(
                    p => p.Status == PaymentTxStatus.SUCCESS);

                // Chỉ tạo refund và set CANCELLED_PENDING_REFUND khi thực sự có tiền hoàn
                if (payment != null && refundPercent > 0)
                {
                    // Dùng invoice.FinalTotal thay vì payment.Amount để nhất quán với GetCancelInfoAsync
                    refundAmount = Math.Round(invoice!.FinalTotal * refundPercent / 100, 0);

                    await _refundRepo.CreateAsync(new Refund
                    {
                        PaymentId = payment.Id,
                        Amount = refundAmount,
                        RefundPercent = refundPercent,
                        Status = RefundStatus.PENDING,
                        CreatedAt = now
                    });

                    // Chỉ set CANCELLED_PENDING_REFUND khi thực sự có tiền cần hoàn
                    booking.Status = BookingStatus.CANCELLED_PENDING_REFUND;
                }
                // refundPercent = 0 → giữ CANCELLED, không tạo refund
            }

            await _bookingRepo.UpdateAsync(booking);

            // Gửi email thông báo hủy
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

            // TODO: Broadcast SignalR
        }

        // Lấy thông tin hủy booking theo token (dùng cho khách hàng hủy booking online)
        public async Task<CancelTokenInfoDto> GetCancelInfoAsync(string token)
        {
            var tokenHash = HashToken(token);
            var booking = await _bookingRepo.GetByCancelTokenAsync(tokenHash);

            if (booking == null)
                throw new AppException(404,
                    "Link hủy không hợp lệ hoặc đã hết hạn", ErrorCodes.NotFound);

            if (booking.CancelTokenUsedAt.HasValue)
                throw new AppException(400,
                    "Link hủy đã được sử dụng", ErrorCodes.BadRequest);

            if (booking.CancelTokenExpiresAt < DateTime.UtcNow)
                throw new AppException(400,
                    "Link hủy đã hết hạn", ErrorCodes.BadRequest);

            // Kiểm tra tài khoản có bị khóa không
            if (booking.CustomerId.HasValue && booking.Customer?.Status == UserStatus.LOCKED)
                throw new AppException(403,
                    "Tài khoản bị khóa, vui lòng liên hệ nhân viên để được hỗ trợ",
                    ErrorCodes.AccountLocked);

            // Defensive: Check BookingCourts không empty
            // Các sân trong cùng booking đều có chung StartTime/EndTime
            // → lấy FirstOrDefault() cho thời gian là đúng; CourtNames liệt kê tất cả sân
            var firstCourt = booking.BookingCourts.FirstOrDefault();
            if (firstCourt == null)
                throw new AppException(500, "Booking không có sân nào", ErrorCodes.InternalError);

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
        /// Hủy booking qua cancel token (link hủy trong email)
        /// Flow: Validate → Atomic token consumption → Update booking → Batch update courts → Create refund → Send email
        /// </summary>
        /// <param name="token">Cancel token từ URL (plain text, chưa hash)</param>
        public async Task CancelByTokenAsync(string token)
        {
            // 1. Hash token và tìm booking
            var tokenHash = HashToken(token);
            var booking = await _bookingRepo.GetByCancelTokenAsync(tokenHash);

            if (booking == null)
                throw new AppException(404,
                    "Link hủy không hợp lệ", ErrorCodes.NotFound);

            // 2. Kiểm tra tài khoản có bị khóa không
            if (booking.CustomerId.HasValue && booking.Customer?.Status == UserStatus.LOCKED)
                throw new AppException(403,
                    "Tài khoản bị khóa, vui lòng liên hệ nhân viên",
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
                    "Link hủy đã được sử dụng", ErrorCodes.BadRequest);
            }

            // 5. Reload booking để đảm bảo state fresh sau khi consume token
            booking = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            // 6. Kiểm tra token đã hết hạn chưa (24h hoặc trước giờ chơi)
            if (booking.CancelTokenExpiresAt < now)
                throw new AppException(400,
                    "Link hủy đã hết hạn", ErrorCodes.BadRequest);

            // 7. Kiểm tra trạng thái có thể hủy không
            // Chỉ cho phép hủy CONFIRMED (walk-in) hoặc PAID_ONLINE (online booking đã thanh toán)
            var cancellableStatuses = new[]
            {
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE
            };

            if (!cancellableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Đơn đặt sân không thể hủy ở trạng thái hiện tại",
                    ErrorCodes.BadRequest);

            // 8. Validate booking có courts không (safety check)
            var firstCourt = booking.BookingCourts.FirstOrDefault()
                ?? throw new AppException(500, "Booking không có sân", ErrorCodes.InternalError);

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
                    // ✅ FIX: Check busy courts cho TẤT CẢ courtIds (không chỉ court đầu tiên)
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

            // TODO: Broadcast SignalR để update real-time cho staff
        }

        // Check-in khách hàng đến sân, chỉ cho phép check-in khi booking đang CONFIRMED hoặc PAID_ONLINE
        public async Task CheckInAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            if (booking.Status != BookingStatus.CONFIRMED &&
                booking.Status != BookingStatus.PAID_ONLINE)
                throw new AppException(400,
                    "Chỉ có thể check-in đơn đang xác nhận hoặc đã thanh toán trực tuyến",
                    ErrorCodes.BadRequest);

            // Validate chuyển trạng thái sử dụng BookingStatusTransition helper
            BookingStatusTransition.ValidateTransition(booking.Status, BookingStatus.IN_PROGRESS);

            var now = DateTime.UtcNow;
            
            booking.Status = BookingStatus.IN_PROGRESS;
            booking.CheckedInAt = now;
            
            // Vô hiệu hóa cancel token khi check-in để khách không thể hủy khi đang chơi
            if (!string.IsNullOrEmpty(booking.CancelTokenHash))
            {
                booking.CancelTokenUsedAt = now;
            }
            
            booking.UpdatedAt = now;
            await _bookingRepo.UpdateAsync(booking);

            // Cập nhật court → IN_USE
            // bc.Court đã được load sẵn qua GetByIdWithDetailsAsync().ThenInclude
            foreach (var bc in booking.BookingCourts)
            {
                var court = bc.Court;
                if (court != null)
                {
                    court.Status = CourtStatus.IN_USE;
                    court.UpdatedAt = now;
                    await _courtRepo.UpdateAsync(court);
                }
            }

            // TODO: Broadcast SignalR
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

            // Bọc toàn bộ checkout logic trong transaction để đảm bảo atomicity
            // Tránh trường hợp: payment created nhưng booking status update fail
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // 1. Conditional update booking status TRƯỚC (DB-level concurrency control)
                // Chỉ 1 trong 2 concurrent requests sẽ thành công
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

                // 2. Tạo Payment record CASH cho phần chưa thu tiền
                // Dùng latestInvoice để đảm bảo FinalTotal và ServiceFee là mới nhất
                // - Invoice UNPAID (walk-in chưa thu tiền): thu toàn bộ FinalTotal
                // - Invoice PARTIALLY_PAID (online có service fee phát sinh): thu phần ServiceFee
                // - Invoice PAID (trả trước đủ): không cần tạo thêm
                if (latestInvoice.PaymentStatus == InvoicePaymentStatus.UNPAID)
                {
                    await _paymentRepo.CreateAsync(new Payment
                    {
                        InvoiceId = latestInvoice.Id,
                        Method = PaymentTxMethod.CASH,
                        Amount = latestInvoice.FinalTotal,  // ← Dùng latest
                        Status = PaymentTxStatus.SUCCESS,
                        PaidAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
                else if (latestInvoice.PaymentStatus == InvoicePaymentStatus.PARTIALLY_PAID
                         && latestInvoice.ServiceFee > 0)
                {
                    // Đã thanh toán sân online (PARTIALLY_PAID), còn lại service fee thu tại quầy
                    await _paymentRepo.CreateAsync(new Payment
                    {
                        InvoiceId = latestInvoice.Id,
                        Method = PaymentTxMethod.CASH,
                        Amount = latestInvoice.ServiceFee,  // ← Dùng latest
                        Status = PaymentTxStatus.SUCCESS,
                        PaidAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }

                // 3. Cập nhật invoice → PAID
                latestInvoice.PaymentStatus = InvoicePaymentStatus.PAID;
                latestInvoice.UpdatedAt = now;
                await _invoiceRepo.UpdateAsync(latestInvoice);

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

            // TODO: Broadcast SignalR
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

            // Wrap toàn bộ logic trong transaction để đảm bảo atomicity
            // Nếu update invoice fail → rollback service insert
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
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
            return MapToDto(result!);
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
                .FirstOrDefault(bs => bs.Id == serviceId);

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
                return MapToDto(currentBooking!);
            }

            // Wrap toàn bộ logic trong transaction để đảm bảo atomicity
            // Nếu update invoice fail → rollback service delete
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
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
            return MapToDto(result!);
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

            // 4. Tìm refund record
            var refund = await _refundRepo.GetByBookingIdAsync(id);
            if (refund == null)
                throw new AppException(404, "Không tìm thấy bản ghi hoàn tiền", ErrorCodes.NotFound);

            var now = DateTime.UtcNow;

            // 5. Transaction scope - đảm bảo tất cả DB operations là atomic
            using (var transaction = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            {
                // 5.1. Update refund status → COMPLETED
                refund.Status = RefundStatus.COMPLETED;
                refund.ProcessedBy = confirmedBy;
                refund.ProcessedAt = now;
                await _refundRepo.UpdateAsync(refund);

                // 5.2. Update payment refunded_amount
                refund.Payment.RefundedAmount = refund.Amount;
                await _paymentRepo.UpdateAsync(refund.Payment);

                // 5.3. Update invoice payment status → REFUNDED
                var invoice = booking.Invoice!;
                invoice.PaymentStatus = InvoicePaymentStatus.REFUNDED;
                invoice.UpdatedAt = now;
                await _invoiceRepo.UpdateAsync(invoice);

                // 5.4. Update booking status → CANCELLED_REFUNDED
                booking.Status = BookingStatus.CANCELLED_REFUNDED;
                booking.UpdatedAt = now;
                await _bookingRepo.UpdateAsync(booking);

                // 5.5. Trừ điểm loyalty theo % refund (chỉ khi có customer và refund > 0)
                // Ví dụ: Refund 50% → trừ 50% điểm đã cộng
                if (booking.CustomerId.HasValue && refund.RefundPercent > 0)
                    await DeductLoyaltyPointsAsync(booking, refund.RefundPercent);

                // 5.6. Commit transaction
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
                        refund.Amount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send refund email for booking {Id}", booking.Id);
            }
        }

        // Kiểm tra quyền thao tác chi nhánh của user, nếu là OWNER thì bỏ qua
        private async Task ValidateBranchAccessAsync(
            Guid branchId, Guid userId, string userRole)
        {
            if (userRole == UserRole.OWNER.ToString()) return;

            var isInBranch = await _userBranchRepo.IsUserInBranchAsync(userId, branchId);
            if (!isInBranch)
                throw new AppException(403,
                    "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden);
        }

        // Tính phần trăm hoàn tiền dựa trên cancel policy
        private async Task<decimal> CalculateRefundPercentAsync(
            TimeOnly startTime, DateOnly bookingDate)
        {
            // lấy thời gian hiện tại ở VN để tính số giờ còn lại trước khi bắt đầu booking
            var bookingDateTime = bookingDate.ToDateTime(startTime);
            var vnNow = DateTimeHelper.GetNowInVietnam();
            
            var hoursUntilStart = (bookingDateTime - vnNow).TotalHours;

            // Đã qua giờ bắt đầu → không hoàn tiền
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
            // Rule 1: Đã thanh toán đủ → KHÔNG cho phép chỉnh sửa
            if (invoice.PaymentStatus == InvoicePaymentStatus.PAID)
                return false;

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
            // 🔴 PRIORITY 1: Financial Truth (Payment Status Check)
            // CRITICAL: Check này PHẢI đi trước vì PaymentStatus là source of truth cuối cùng
            // Ngăn modify sau khi đã thu tiền - quan trọng nhất về mặt tài chính
            // Case: Status = PENDING_PAYMENT + PaymentStatus = PAID → PHẢI block (đã thu tiền rồi)
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
                
                // ✅ Variables để lưu tier info cho email (fix bug: loyalty object không được refresh)
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

                // ✅ FIX: Gửi email thông báo lên hạng (NGOÀI transaction - không ảnh hưởng nếu fail)
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
                using (var transaction = new System.Transactions.TransactionScope(
                    System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
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
                    catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("ux_loyalty_deduct_booking") == true)
                    {
                        // Unique index violation - duplicate deduction detected
                        // Another request already processed this deduction
                        _logger.LogWarning(
                            "[LOYALTY] Duplicate deduction detected for booking {BookingId} (caught by unique index). " +
                            "Another request already processed this deduction. Skipping.",
                            booking.Id);
                        return; // Skip gracefully - không rollback vì đã có request khác xử lý
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

                // Tạo cancel token
                var rawToken = GenerateCancelToken();
                var tokenHash = HashToken(rawToken);

                // DTO đã validate các sân đều có chung thời gian (StartTime, EndTime)
                var startTime = TimeOnly.FromTimeSpan(courts.First().Slot.StartTime);
                var endTime = TimeOnly.FromTimeSpan(courts.First().Slot.EndTime);

                // Lấy VN time để nhất quán với PaymentService.SendConfirmationWithCancelTokenAsync
                var tokenExpiry = new DateTime[] {
                    booking.BookingDate.ToDateTime(startTime),
                    DateTimeHelper.GetNowInVietnam().AddHours(24)
                }.Min();

                booking.CancelTokenHash = tokenHash;
                booking.CancelTokenExpiresAt = tokenExpiry;
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

        // map data từ entity Booking sang DTO BookingDto, bao gồm courts, price items và services
        private static BookingDto MapToDto(Booking b) => new()
        {
            Id = b.Id,
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
        private static string RemoveDiacritics(string text)
        {
            // Xử lý các ký tự đặc biệt không decompose được trong NFD
            text = text.Replace("Đ", "D").Replace("đ", "d");

            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder();
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        // Logic chung dùng để tạo chi tiết của một Booking
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
                    DiscountRateSnapshot = promotion.DiscountRate,
                    DiscountAmount = promotionDiscountAmount,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var invoice = await _invoiceRepo.CreateAsync(new Invoice
            {
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
    }
}
