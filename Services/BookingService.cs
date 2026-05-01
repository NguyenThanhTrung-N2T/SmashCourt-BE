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
            ILogger<BookingService> logger)
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
            if (invoice?.PaymentStatus != InvoicePaymentStatus.UNPAID)
            {
                var refundPercent = await CalculateRefundPercentAsync(
                    booking.BookingCourts.First().StartTime, booking.BookingDate);

                var payment = invoice?.Payments?.FirstOrDefault(
                    p => p.Status == PaymentTxStatus.SUCCESS);

                // Chỉ tạo refund và set CANCELLED_PENDING_REFUND khi thực sự có tiền hoàn
                if (payment != null && refundPercent > 0)
                {
                    // Dùng invoice.FinalTotal thay vì payment.Amount để nhất quán với GetCancelInfoAsync
                    var refundAmount = Math.Round(invoice!.FinalTotal * refundPercent / 100, 0);

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
                    await _emailService.SendCancelConfirmationAsync(email, name!, booking.Id);
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

            // Các sân trong cùng booking đều có chung StartTime/EndTime
            // → lấy First() cho thời gian là đúng; CourtNames liệt kê tất cả sân
            var firstCourt = booking.BookingCourts.First();
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

        // Hủy booking theo token (dùng cho khách hàng hủy booking online)
        public async Task CancelByTokenAsync(string token)
        {
            var tokenHash = HashToken(token);
            var booking = await _bookingRepo.GetByCancelTokenAsync(tokenHash);

            if (booking == null)
                throw new AppException(404,
                    "Link hủy không hợp lệ", ErrorCodes.NotFound);

            if (booking.CancelTokenUsedAt.HasValue)
                throw new AppException(400,
                    "Link hủy đã được sử dụng", ErrorCodes.BadRequest);

            if (booking.CancelTokenExpiresAt < DateTime.UtcNow)
                throw new AppException(400,
                    "Link hủy đã hết hạn", ErrorCodes.BadRequest);

            if (booking.CustomerId.HasValue && booking.Customer?.Status == UserStatus.LOCKED)
                throw new AppException(403,
                    "Tài khoản bị khóa, vui lòng liên hệ nhân viên",
                    ErrorCodes.AccountLocked);

            // Token chỉ được tạo sau khi booking CONFIRMED (walk-in) hoặc PAID_ONLINE (online).
            // Các trạng thái khác đều không hợp lệ để hủy qua link:
            //   — PENDING       : chưa có token (token chưa được sinh)
            //   — IN_PROGRESS   : đang chơi, không cho hủy
            //   — PENDING_PAYMENT / COMPLETED : đã kết thúc
            //   — CANCELLED*   : đã hủy trước đó
            var cancellableStatuses = new[]
            {
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE
            };

            if (!cancellableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Đơn đặt sân không thể hủy ở trạng thái hiện tại",
                    ErrorCodes.BadRequest);

            var now = DateTime.UtcNow;
            var invoice = booking.Invoice;

            // Set CANCELLED trước (default)
            booking.Status = BookingStatus.CANCELLED;
            booking.CancelledAt = now;
            booking.CancelSource = CancelSourceEnum.LINK;
            booking.CancelTokenUsedAt = now;
            booking.UpdatedAt = now;

            // Dùng repository call rõ ràng thay vì set trực tiếp trên RAM (nhất quán với CancelByStaffAsync)
            await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

            await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

            // bc.Court đã được load sẵn qua GetByCancelTokenAsync().ThenInclude
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
            if (invoice?.PaymentStatus != InvoicePaymentStatus.UNPAID)
            {
                var refundPercent = await CalculateRefundPercentAsync(
                    booking.BookingCourts.First().StartTime, booking.BookingDate);

                var payment = invoice?.Payments?.FirstOrDefault(
                    p => p.Status == PaymentTxStatus.SUCCESS);

                // Chỉ tạo refund và set CANCELLED_PENDING_REFUND khi thực sự có tiền hoàn
                if (payment != null && refundPercent > 0)
                {
                    // Dùng invoice.FinalTotal thay vì payment.Amount để nhất quán với GetCancelInfoAsync
                    var refundAmount = Math.Round(invoice!.FinalTotal * refundPercent / 100, 0);

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
                    await _emailService.SendCancelConfirmationAsync(email, name!, booking.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancel email for booking {BookingId}", booking.Id);
            }

            // TODO: Broadcast SignalR
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

            // Tạo Payment record CASH cho phần chưa thu tiền
            // - Invoice UNPAID (walk-in chưa thu tiền): thu toàn bộ FinalTotal
            // - Invoice PARTIALLY_PAID (online có service fee phát sinh): thu phần ServiceFee
            // - Invoice PAID (trả trước đủ): không cần tạo thêm
            if (invoice.PaymentStatus == InvoicePaymentStatus.UNPAID)
            {
                await _paymentRepo.CreateAsync(new Payment
                {
                    InvoiceId = invoice.Id,
                    Method = PaymentTxMethod.CASH,
                    Amount = invoice.FinalTotal,
                    Status = PaymentTxStatus.SUCCESS,
                    PaidAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else if (invoice.PaymentStatus == InvoicePaymentStatus.PARTIALLY_PAID
                     && invoice.ServiceFee > 0)
            {
                // Đã thanh toán sân online (PARTIALLY_PAID), còn lại service fee thu tại quầy
                await _paymentRepo.CreateAsync(new Payment
                {
                    InvoiceId = invoice.Id,
                    Method = PaymentTxMethod.CASH,
                    Amount = invoice.ServiceFee,
                    Status = PaymentTxStatus.SUCCESS,
                    PaidAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            // Cập nhật invoice → PAID
            invoice.PaymentStatus = InvoicePaymentStatus.PAID;
            invoice.UpdatedAt = now;
            await _invoiceRepo.UpdateAsync(invoice);

            // Cập nhật booking → COMPLETED
            booking.Status = BookingStatus.COMPLETED;
            booking.UpdatedAt = now;
            
            // Deactivate booking courts khi COMPLETED
            await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);
            
            await _bookingRepo.UpdateAsync(booking);

            // Cập nhật court → AVAILABLE
            // bc.Court đã được load sẵn qua GetByIdWithDetailsAsync().ThenInclude
            foreach (var bc in booking.BookingCourts)
            {
                var court = bc.Court;
                if (court != null)
                {
                    court.Status = CourtStatus.AVAILABLE;
                    court.UpdatedAt = now;
                    await _courtRepo.UpdateAsync(court);
                }
            }

            // Tích điểm loyalty nếu có tài khoản
            if (booking.CustomerId.HasValue)
                await EarnLoyaltyPointsAsync(booking, invoice.CourtFee);

            // TODO: Broadcast SignalR
        }

        // thêm dịch vụ vào booking, chỉ cho phép thêm khi booking đang CONFIRMED, IN_PROGRESS hoặc PENDING_PAYMENT
        public async Task<BookingDto> AddServiceAsync(
            Guid id, AddBookingServiceDto dto,
            Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            var allowedStatuses = new[]
            {
                BookingStatus.CONFIRMED,
                BookingStatus.IN_PROGRESS,
                BookingStatus.PENDING_PAYMENT
            };

            if (!allowedStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Không thể thêm dịch vụ ở trạng thái này", ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

            if (invoice.PaymentStatus == InvoicePaymentStatus.PAID)
                throw new AppException(400,
                    "Hóa đơn đã thanh toán, không thể thêm dịch vụ", ErrorCodes.BadRequest);

            // PARTIALLY_PAID chỉ cho thêm khi IN_PROGRESS
            if (invoice?.PaymentStatus == InvoicePaymentStatus.PARTIALLY_PAID &&
                booking.Status != BookingStatus.IN_PROGRESS)
                throw new AppException(400,
                    "Chỉ có thể thêm dịch vụ khi đang tiến hành", ErrorCodes.BadRequest);

            // Tìm branch service
            var branchService = await _branchServiceRepo.GetByBranchServiceAsync(
                booking.BranchId, dto.ServiceId);

            if (branchService == null ||
                branchService.Status != BranchServiceStatus.ENABLED ||
                branchService.Service.Status != ServiceStatus.ACTIVE)
                throw new AppException(400,
                    "Dịch vụ không tồn tại hoặc đã bị tắt", ErrorCodes.BadRequest);

            // Snapshot giá + tên tại thời điểm thêm
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

            // Cập nhật service_fee trong invoice
            var serviceFeeTotal = booking.BookingServices.Sum(s => s.UnitPrice * s.Quantity)
                                + branchService.Price * dto.Quantity;

            invoice!.ServiceFee = serviceFeeTotal;
            invoice.FinalTotal = invoice.CourtFee
                               - invoice.LoyaltyDiscountAmount
                               - invoice.PromotionDiscountAmount
                               + serviceFeeTotal;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _invoiceRepo.UpdateAsync(invoice);

            var result = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);
            return MapToDto(result!);
        }

        // Xóa dịch vụ khỏi booking, chỉ cho phép xóa khi booking đang CONFIRMED, IN_PROGRESS hoặc PENDING_PAYMENT
        public async Task<BookingDto> RemoveServiceAsync(
            Guid id, Guid serviceId,
            Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            var allowedStatuses = new[]
            {
                BookingStatus.CONFIRMED,
                BookingStatus.IN_PROGRESS,
                BookingStatus.PENDING_PAYMENT
            };

            if (!allowedStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Không thể xóa dịch vụ ở trạng thái này", ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

            if (invoice.PaymentStatus == InvoicePaymentStatus.PAID)
                throw new AppException(400,
                    "Hóa đơn đã thanh toán, không thể xóa dịch vụ", ErrorCodes.BadRequest);

            // PARTIALLY_PAID chỉ cho xóa khi IN_PROGRESS
            if (invoice?.PaymentStatus == InvoicePaymentStatus.PARTIALLY_PAID &&
                booking.Status != BookingStatus.IN_PROGRESS)
                throw new AppException(400,
                    "Chỉ có thể xóa dịch vụ khi đang IN_PROGRESS", ErrorCodes.BadRequest);

            var bookingService = booking.BookingServices
                .FirstOrDefault(bs => bs.Id == serviceId);

            if (bookingService == null)
                throw new AppException(404, "Không tìm thấy dịch vụ", ErrorCodes.NotFound);

            await _bookingRepo.RemoveServiceAsync(bookingService);

            // Tái tính invoice
            var remainingServiceFee = booking.BookingServices
                .Where(bs => bs.Id != serviceId)
                .Sum(s => s.UnitPrice * s.Quantity);

            invoice!.ServiceFee = remainingServiceFee;
            invoice.FinalTotal = invoice.CourtFee
                               - invoice.LoyaltyDiscountAmount
                               - invoice.PromotionDiscountAmount
                               + remainingServiceFee;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _invoiceRepo.UpdateAsync(invoice);

            var result = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);
            return MapToDto(result!);
        }

        // Xác nhận hoàn tiền bởi nhân viên
        public async Task ConfirmRefundAsync(
            Guid id, Guid confirmedBy, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            // kiểm tra quyền thao tác chi nhánh của user, nếu là OWNER thì bỏ qua
            await ValidateBranchAccessAsync(booking.BranchId, confirmedBy, currentUserRole);

            if (booking.Status != BookingStatus.CANCELLED_PENDING_REFUND)
                throw new AppException(400,
                    "Đơn không ở trạng thái chờ hoàn tiền", ErrorCodes.BadRequest);

            var refund = await _refundRepo.GetByBookingIdAsync(id);
            if (refund == null)
                throw new AppException(404, "Không tìm thấy bản ghi hoàn tiền", ErrorCodes.NotFound);

            var now = DateTime.UtcNow;

            refund.Status = RefundStatus.COMPLETED;
            refund.ProcessedBy = confirmedBy;
            refund.ProcessedAt = now;
            await _refundRepo.UpdateAsync(refund);

            // Cập nhật payment refunded_amount
            refund.Payment.RefundedAmount = refund.Amount;
            await _paymentRepo.UpdateAsync(refund.Payment);

            // Cập nhật invoice → REFUNDED
            var invoice = booking.Invoice!;
            invoice.PaymentStatus = InvoicePaymentStatus.REFUNDED;
            invoice.UpdatedAt = now;
            await _invoiceRepo.UpdateAsync(invoice);

            // Cập nhật booking → CANCELLED_REFUNDED
            booking.Status = BookingStatus.CANCELLED_REFUNDED;
            booking.UpdatedAt = now;
            await _bookingRepo.UpdateAsync(booking);

            // Gửi email thông báo hoàn tiền cho khách
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (!string.IsNullOrEmpty(email))
                    await _emailService.SendRefundConfirmedAsync(
                        email, name!, booking.Id, refund.Amount);
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
                loyalty.TotalPoints += pointsEarned;
                loyalty.UpdatedAt = DateTime.UtcNow;

                // Kiểm tra lên hạng loyalty
                var allTiers = await _loyaltyTierRepo.GetAllLoyaltyTiersAsync();
                var newTier = allTiers
                    .Where(t => t.MinPoints <= loyalty.TotalPoints)
                    .OrderByDescending(t => t.MinPoints)
                    .FirstOrDefault();

                if (newTier != null && newTier.Id != loyalty.TierId)
                    loyalty.TierId = newTier.Id;

                await _loyaltyRepo.UpdateAsync(loyalty);

                // Ghi transaction — dùng AddAsync đã thêm vào interface
                await _loyaltyTransactionRepo.AddAsync(new LoyaltyTransaction
                {
                    UserId = booking.CustomerId!.Value,
                    BookingId = booking.Id,
                    Points = pointsEarned,
                    TotalPointsAfter = loyalty.TotalPoints,
                    Type = LoyaltyTransactionType.EARN,
                    CreatedAt = DateTime.UtcNow
                });

                // Gửi email thông báo lên hạng
                if (newTier != null && newTier.Id != tierBefore)
                {
                    try
                    {
                        var user = await _userRepo.GetUserByIdAsync(booking.CustomerId!.Value);
                        if (user != null)
                        {
                            await _emailService.SendTierUpgradeAsync(
                                user.Email, user.FullName, newTier.Name);
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

                // Build email model using Factory
                var emailModel = BookingEmailFactory.Build(booking, rawToken);
                
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
