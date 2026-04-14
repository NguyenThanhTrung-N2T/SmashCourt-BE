using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.IService;
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

        // ── GET ALL (staff/owner) ─────────────────────────────────────────────
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

        // ── GET MY BOOKINGS (customer) ────────────────────────────────────────
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

        // ── GET BY ID ─────────────────────────────────────────────────────────
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

        // ── CREATE ONLINE ─────────────────────────────────────────────────────
        public async Task<OnlineBookingResponse> CreateOnlineAsync(
            CreateOnlineBookingDto dto, Guid? customerId)
        {
            // 1. Tìm court — không truyền branchId vì chưa biết, repo sẽ bỏ qua filter branch
            var court = await _courtRepo.GetByIdAsync(dto.CourtId);
            if (court == null || court.Status == CourtStatus.INACTIVE)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            if (court.Status == CourtStatus.SUSPENDED)
                throw new AppException(400,
                    "Sân đang tạm ngưng hoạt động", ErrorCodes.BadRequest);

            // 2. Validate khách vãng lai
            if (customerId == null &&
                (string.IsNullOrEmpty(dto.GuestName) ||
                 string.IsNullOrEmpty(dto.GuestPhone) ||
                 string.IsNullOrEmpty(dto.GuestEmail)))
                throw new AppException(400,
                    "Vui lòng nhập đầy đủ họ tên, SĐT và email", ErrorCodes.BadRequest);

            // 3. Check slot trống
            var hasOverlap = await _bookingRepo.HasOverlapAsync(
                dto.CourtId, dto.BookingDate, dto.StartTime, dto.EndTime);
            if (hasOverlap)
                throw new AppException(400,
                    "Slot này đã được đặt, vui lòng chọn slot khác", ErrorCodes.BadRequest);

            // 4. Check + xóa slot_lock cũ
            await _slotLockRepo.DeleteExpiredAsync(dto.CourtId);
            var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                dto.CourtId, dto.BookingDate, dto.StartTime, dto.EndTime);
            if (existingLock != null)
            {
                var remaining = (int)(existingLock.ExpiresAt - DateTime.UtcNow).TotalMinutes;
                throw new AppException(400,
                    $"Slot này đang bị khóa bởi giao dịch online, còn khoảng {remaining} phút",
                    ErrorCodes.BadRequest);
            }

            // 5. Tính giá
            var priceResult = await _priceService.CalculateAsync(
                court.BranchId,
                new CalculatePriceDto
                {
                    CourtId = dto.CourtId,
                    BookingDate = dto.BookingDate,
                    StartTime = dto.StartTime,
                    EndTime = dto.EndTime
                });

            var courtFee = priceResult.CourtFee;

            // 6. Loyalty discount
            decimal loyaltyDiscountAmount = 0;
            if (customerId.HasValue)
            {
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(customerId.Value);
                if (loyalty?.Tier != null)
                    loyaltyDiscountAmount = Math.Round(
                        courtFee * loyalty.Tier.DiscountRate / 100, 0);
            }

            var courtFeeAfterLoyalty = courtFee - loyaltyDiscountAmount;

            // 7. Promotion discount
            decimal promotionDiscountAmount = 0;
            Promotion? promotion = null;
            if (dto.PromotionId.HasValue && customerId.HasValue)
            {
                promotion = await _promotionRepo.GetByIdAsync(dto.PromotionId.Value);
                if (promotion == null || promotion.Status != PromotionStatus.ACTIVE)
                    throw new AppException(400,
                        "Khuyến mãi không hợp lệ hoặc đã hết hạn", ErrorCodes.BadRequest);

                promotionDiscountAmount = Math.Round(
                    courtFeeAfterLoyalty * promotion.DiscountRate / 100, 0);
            }

            var finalTotal = courtFeeAfterLoyalty - promotionDiscountAmount;

            // 8. Tạo booking PENDING
            var expiresAt = DateTime.UtcNow.AddMinutes(10);
            var booking = new Booking
            {
                BranchId = court.BranchId,
                CustomerId = customerId,
                GuestName = dto.GuestName?.Trim(),
                GuestPhone = dto.GuestPhone?.Trim(),
                GuestEmail = dto.GuestEmail?.Trim(),
                BookingDate = dto.BookingDate,
                Status = BookingStatus.PENDING,
                Source = BookingSource.ONLINE,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            booking = await _bookingRepo.CreateAsync(booking);

            // 9. Tạo BookingCourt — save để lấy Id
            var bookingCourt = await _bookingRepo.AddCourtAsync(new BookingCourt
            {
                BookingId = booking.Id,
                CourtId = dto.CourtId,
                Date = dto.BookingDate,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            // 10. Tạo PriceItems — cần bookingCourt.Id
            var dayType = dto.BookingDate.DayOfWeek == DayOfWeek.Saturday ||
                          dto.BookingDate.DayOfWeek == DayOfWeek.Sunday
                ? DayType.WEEKEND : DayType.WEEKDAY;

            var allSlots = await _timeSlotRepo.GetAllAsync();
            var priceItems = new List<BookingPriceItem>();

            foreach (var item in priceResult.Breakdown)
            {
                var timeSlot = allSlots.FirstOrDefault(ts =>
                    ts.StartTime == item.StartTime &&
                    ts.EndTime == item.EndTime &&
                    ts.DayType == dayType);

                if (timeSlot != null)
                {
                    priceItems.Add(new BookingPriceItem
                    {
                        BookingCourtId = bookingCourt.Id,
                        TimeSlotId = timeSlot.Id,
                        UnitPrice = item.UnitPrice,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            await _bookingRepo.AddPriceItemsAsync(priceItems);

            // 11. Tạo BookingPromotion
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

            // 12. Tạo Invoice
            await _invoiceRepo.CreateAsync(new Invoice
            {
                BookingId = booking.Id,
                CourtFee = courtFee,
                ServiceFee = 0,
                LoyaltyDiscountAmount = loyaltyDiscountAmount,
                PromotionDiscountAmount = promotionDiscountAmount,
                FinalTotal = finalTotal,
                PaymentStatus = InvoicePaymentStatus.UNPAID,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // 13. Tạo SlotLock
            await _slotLockRepo.CreateAsync(new SlotLock
            {
                CourtId = dto.CourtId,
                BookingId = booking.Id,
                Date = dto.BookingDate,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            });

            // 14. Court → LOCKED
            court.Status = CourtStatus.LOCKED;
            court.UpdatedAt = DateTime.UtcNow;
            await _courtRepo.UpdateAsync(court);

            // 15. Tạo Payment + lấy VNPay URL
            var transactionRef = $"SC_{booking.Id:N}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var paymentUrl = _vnPayService.CreatePaymentUrl(
                transactionRef, finalTotal, $"Dat san {court.Name}");

            await _paymentRepo.CreateAsync(new Payment
            {
                InvoiceId = (await _invoiceRepo.GetByBookingIdAsync(booking.Id))!.Id,
                Method = PaymentTxMethod.VNPAY,
                Amount = finalTotal,
                Status = PaymentTxStatus.PENDING,
                TransactionRef = transactionRef,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            return new OnlineBookingResponse
            {
                BookingId = booking.Id,
                PaymentUrl = paymentUrl,
                ExpiresAt = expiresAt,
                FinalTotal = finalTotal
            };
        }

        // ── CREATE WALK-IN ────────────────────────────────────────────────────
        public async Task<BookingDto> CreateWalkInAsync(
            CreateWalkInBookingDto dto, Guid createdBy)
        {
            // 1. Tìm court — không truyền branchId vì chưa biết, repo sẽ bỏ qua filter branch
            var court = await _courtRepo.GetByIdAsync(dto.CourtId);
            if (court == null || court.Status == CourtStatus.INACTIVE)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            if (court.Status == CourtStatus.SUSPENDED)
                throw new AppException(400, "Sân đang tạm ngưng hoạt động", ErrorCodes.BadRequest);

            // 2. Check slot còn trống
            var hasOverlap = await _bookingRepo.HasOverlapAsync(
                dto.CourtId, dto.BookingDate, dto.StartTime, dto.EndTime);
            if (hasOverlap)
                throw new AppException(400,
                    "Slot này đã được đặt", ErrorCodes.BadRequest);

            // 3. Check slot_lock — ngăn đặt đè lên giao dịch online đang chờ thanh toán
            await _slotLockRepo.DeleteExpiredAsync(dto.CourtId);
            var existingLock = await _slotLockRepo.GetByCourtAndTimeAsync(
                dto.CourtId, dto.BookingDate, dto.StartTime, dto.EndTime);
            if (existingLock != null)
            {
                var remaining = (int)(existingLock.ExpiresAt - DateTime.UtcNow).TotalMinutes;
                throw new AppException(400,
                    $"Slot này đang bị khóa bởi giao dịch online, còn khoảng {remaining} phút",
                    ErrorCodes.BadRequest);
            }

            // 4. Tính giá
            var priceResult = await _priceService.CalculateAsync(
                court.BranchId,
                new CalculatePriceDto
                {
                    CourtId = dto.CourtId,
                    BookingDate = dto.BookingDate,
                    StartTime = dto.StartTime,
                    EndTime = dto.EndTime
                });

            var courtFee = priceResult.CourtFee;

            // 4. Tính loyalty + promotion (chỉ khi có customerId)
            decimal loyaltyDiscountAmount = 0;
            decimal promotionDiscountAmount = 0;
            Promotion? promotion = null;

            if (dto.CustomerId.HasValue)
            {
                var loyalty = await _loyaltyRepo.GetByUserIdAsync(dto.CustomerId.Value);
                if (loyalty?.Tier != null)
                    loyaltyDiscountAmount = Math.Round(
                        courtFee * loyalty.Tier.DiscountRate / 100, 0);

                if (dto.PromotionId.HasValue)
                {
                    promotion = await _promotionRepo.GetByIdAsync(dto.PromotionId.Value);
                    if (promotion == null || promotion.Status != PromotionStatus.ACTIVE)
                        throw new AppException(400,
                            "Khuyến mãi không hợp lệ", ErrorCodes.BadRequest);

                    promotionDiscountAmount = Math.Round(
                        (courtFee - loyaltyDiscountAmount) * promotion.DiscountRate / 100, 0);
                }
            }

            var finalTotal = courtFee - loyaltyDiscountAmount - promotionDiscountAmount;

            // 5. Tạo booking CONFIRMED
            var booking = new Booking
            {
                BranchId = court.BranchId,
                CustomerId = dto.CustomerId,
                GuestName = dto.GuestName?.Trim(),
                GuestPhone = dto.GuestPhone?.Trim(),
                GuestEmail = dto.GuestEmail?.Trim(),
                BookingDate = dto.BookingDate,
                Status = BookingStatus.CONFIRMED,
                Source = BookingSource.WALK_IN,
                Note = dto.Note?.Trim(),
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            booking = await _bookingRepo.CreateAsync(booking);

            // 6. Tạo BookingCourt qua repo — lấy Id để dùng cho price items
            var bookingCourt = await _bookingRepo.AddCourtAsync(new BookingCourt
            {
                BookingId = booking.Id,
                CourtId = dto.CourtId,
                Date = dto.BookingDate,
                StartTime = dto.StartTime,
                EndTime = dto.EndTime,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            // 7. Tạo PriceItems qua repo
            var dayType = dto.BookingDate.DayOfWeek == DayOfWeek.Saturday ||
                          dto.BookingDate.DayOfWeek == DayOfWeek.Sunday
                ? DayType.WEEKEND : DayType.WEEKDAY;

            var allSlots = await _timeSlotRepo.GetAllAsync();
            var priceItems = new List<BookingPriceItem>();

            foreach (var item in priceResult.Breakdown)
            {
                var timeSlot = allSlots.FirstOrDefault(ts =>
                    ts.StartTime == item.StartTime &&
                    ts.EndTime == item.EndTime &&
                    ts.DayType == dayType);

                if (timeSlot != null)
                {
                    priceItems.Add(new BookingPriceItem
                    {
                        BookingCourtId = bookingCourt.Id,
                        TimeSlotId = timeSlot.Id,
                        UnitPrice = item.UnitPrice,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            await _bookingRepo.AddPriceItemsAsync(priceItems);

            // 8. Tạo promotion nếu có
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

            // 9. Tạo invoice UNPAID
            await _invoiceRepo.CreateAsync(new Invoice
            {
                BookingId = booking.Id,
                CourtFee = courtFee,
                ServiceFee = 0,
                LoyaltyDiscountAmount = loyaltyDiscountAmount,
                PromotionDiscountAmount = promotionDiscountAmount,
                FinalTotal = finalTotal,
                PaymentStatus = InvoicePaymentStatus.UNPAID,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // 10. Cập nhật court → BOOKED
            court.Status = CourtStatus.BOOKED;
            court.UpdatedAt = DateTime.UtcNow;
            await _courtRepo.UpdateAsync(court);

            // 11. Gửi email xác nhận + link hủy
            await SendConfirmationEmailAsync(booking, court, dto.StartTime, dto.EndTime);

            var result = await _bookingRepo.GetByIdWithDetailsAsync(booking.Id);
            return MapToDto(result!);
        }

        // ── CANCEL BY STAFF ───────────────────────────────────────────────────
        public async Task CancelByStaffAsync(
            Guid id, Guid cancelledBy, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            // Validate branch access
            await ValidateBranchAccessAsync(booking.BranchId, cancelledBy, currentUserRole);

            var cancellableStatuses = new[]
            {
                BookingStatus.PENDING,
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE,
                BookingStatus.IN_PROGRESS
            };

            if (!cancellableStatuses.Contains(booking.Status))
                throw new AppException(400,
                    "Không thể hủy đơn ở trạng thái này", ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            var now = DateTime.UtcNow;

            booking.Status = invoice?.PaymentStatus == InvoicePaymentStatus.UNPAID
                ? BookingStatus.CANCELLED
                : BookingStatus.CANCELLED_PENDING_REFUND;

            booking.CancelledBy = cancelledBy;
            booking.CancelledAt = now;
            booking.CancelSource = CancelSourceEnum.STAFF;
            booking.UpdatedAt = now;

            // Deactivate booking_courts
            await _bookingRepo.UpdateCourtActiveStatusAsync(booking.Id, false);

            // Xóa slot_lock nếu có
            await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

            // Cập nhật court → AVAILABLE
            var firstCourt = booking.BookingCourts.FirstOrDefault();
            if (firstCourt != null)
            {
                var court = await _courtRepo.GetByIdAsync(firstCourt.CourtId);
                if (court != null)
                {
                    court.Status = CourtStatus.AVAILABLE;
                    court.UpdatedAt = now;
                    await _courtRepo.UpdateAsync(court);
                }
            }

            // Tạo refund record nếu đã có tiền
            if (booking.Status == BookingStatus.CANCELLED_PENDING_REFUND && invoice != null)
            {
                var refundPercent = await CalculateRefundPercentAsync(
                    booking.BookingCourts.First().StartTime, booking.BookingDate);

                var payment = invoice.Payments?.FirstOrDefault(
                    p => p.Status == PaymentTxStatus.SUCCESS);

                if (payment != null)
                {
                    await _refundRepo.CreateAsync(new Refund
                    {
                        PaymentId = payment.Id,
                        Amount = Math.Round(payment.Amount * refundPercent / 100, 0),
                        RefundPercent = refundPercent,
                        Status = RefundStatus.PENDING,
                        CreatedAt = now
                    });
                }
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

        // ── CANCEL BY TOKEN ───────────────────────────────────────────────────
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

            // Check tài khoản bị khóa
            if (booking.CustomerId.HasValue && booking.Customer?.Status == UserStatus.LOCKED)
                throw new AppException(403,
                    "Tài khoản bị khóa, vui lòng liên hệ nhân viên để được hỗ trợ",
                    ErrorCodes.AccountLocked);

            var bookingCourt = booking.BookingCourts.First();
            var refundPercent = await CalculateRefundPercentAsync(
                bookingCourt.StartTime, booking.BookingDate);

            var invoice = booking.Invoice;
            var refundAmount = invoice != null
                ? Math.Round(invoice.FinalTotal * refundPercent / 100, 0)
                : 0;

            return new CancelTokenInfoDto
            {
                BookingId = booking.Id,
                BranchName = booking.Branch.Name,
                CourtName = bookingCourt.Court.Name,
                BookingDate = booking.BookingDate,
                StartTime = bookingCourt.StartTime,
                EndTime = bookingCourt.EndTime,
                RefundAmount = refundAmount,
                RefundPercent = refundPercent,
                Status = booking.Status.ToString()
            };
        }

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

            var now = DateTime.UtcNow;
            var invoice = booking.Invoice;

            booking.Status = invoice?.PaymentStatus == InvoicePaymentStatus.UNPAID
                ? BookingStatus.CANCELLED
                : BookingStatus.CANCELLED_PENDING_REFUND;

            booking.CancelledAt = now;
            booking.CancelSource = CancelSourceEnum.LINK;
            booking.CancelTokenUsedAt = now;
            booking.UpdatedAt = now;

            foreach (var bc in booking.BookingCourts)
                bc.IsActive = false;

            await _slotLockRepo.DeleteByBookingIdAsync(booking.Id);

            var firstCourt = booking.BookingCourts.FirstOrDefault();
            if (firstCourt != null)
            {
                var court = await _courtRepo.GetByIdAsync(firstCourt.CourtId, booking.BranchId);
                if (court != null)
                {
                    court.Status = CourtStatus.AVAILABLE;
                    court.UpdatedAt = now;
                    await _courtRepo.UpdateAsync(court);
                }
            }

            if (booking.Status == BookingStatus.CANCELLED_PENDING_REFUND && invoice != null)
            {
                var refundPercent = await CalculateRefundPercentAsync(
                    booking.BookingCourts.First().StartTime, booking.BookingDate);

                var payment = invoice.Payments?.FirstOrDefault(
                    p => p.Status == PaymentTxStatus.SUCCESS);

                if (payment != null)
                {
                    await _refundRepo.CreateAsync(new Refund
                    {
                        PaymentId = payment.Id,
                        Amount = Math.Round(payment.Amount * refundPercent / 100, 0),
                        RefundPercent = refundPercent,
                        Status = RefundStatus.PENDING,
                        CreatedAt = now
                    });
                }
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

        // ── CHECK-IN ──────────────────────────────────────────────────────────
        public async Task CheckInAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            if (booking.Status != BookingStatus.CONFIRMED &&
                booking.Status != BookingStatus.PAID_ONLINE)
                throw new AppException(400,
                    "Chỉ có thể check-in đơn đang CONFIRMED hoặc PAID_ONLINE",
                    ErrorCodes.BadRequest);

            booking.Status = BookingStatus.IN_PROGRESS;
            booking.UpdatedAt = DateTime.UtcNow;
            await _bookingRepo.UpdateAsync(booking);

            // Cập nhật court → IN_USE
            var firstCourt = booking.BookingCourts.FirstOrDefault();
            if (firstCourt != null)
            {
                var court = await _courtRepo.GetByIdAsync(firstCourt.CourtId, booking.BranchId);
                if (court != null)
                {
                    court.Status = CourtStatus.IN_USE;
                    court.UpdatedAt = DateTime.UtcNow;
                    await _courtRepo.UpdateAsync(court);
                }
            }

            // TODO: Broadcast SignalR
        }

        // ── CHECKOUT ──────────────────────────────────────────────────────────
        public async Task CheckoutAsync(Guid id, Guid currentUserId, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            await ValidateBranchAccessAsync(booking.BranchId, currentUserId, currentUserRole);

            if (booking.Status != BookingStatus.PENDING_PAYMENT)
                throw new AppException(400,
                    "Chỉ có thể checkout đơn đang PENDING_PAYMENT",
                    ErrorCodes.BadRequest);

            var invoice = booking.Invoice;
            if (invoice == null)
                throw new AppException(500, "Không tìm thấy hóa đơn", ErrorCodes.InternalError);

            // Cập nhật invoice → PAID
            invoice.PaymentStatus = InvoicePaymentStatus.PAID;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _invoiceRepo.UpdateAsync(invoice);

            // Cập nhật booking → COMPLETED
            booking.Status = BookingStatus.COMPLETED;
            booking.UpdatedAt = DateTime.UtcNow;
            await _bookingRepo.UpdateAsync(booking);

            // Cập nhật court → AVAILABLE
            var firstCourt = booking.BookingCourts.FirstOrDefault();
            if (firstCourt != null)
            {
                var court = await _courtRepo.GetByIdAsync(firstCourt.CourtId, booking.BranchId);
                if (court != null)
                {
                    court.Status = CourtStatus.AVAILABLE;
                    court.UpdatedAt = DateTime.UtcNow;
                    await _courtRepo.UpdateAsync(court);
                }
            }

            // Tích điểm loyalty nếu có tài khoản
            if (booking.CustomerId.HasValue)
                await EarnLoyaltyPointsAsync(booking, invoice.CourtFee);

            // TODO: Broadcast SignalR
        }

        // ── ADD SERVICE ───────────────────────────────────────────────────────
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
                    "Chỉ có thể thêm dịch vụ khi đang IN_PROGRESS", ErrorCodes.BadRequest);

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

        // ── REMOVE SERVICE ────────────────────────────────────────────────────
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

        // ── CONFIRM REFUND ────────────────────────────────────────────────────
        public async Task ConfirmRefundAsync(
            Guid id, Guid confirmedBy, string currentUserRole)
        {
            var booking = await _bookingRepo.GetByIdWithDetailsAsync(id);
            if (booking == null)
                throw new AppException(404, "Không tìm thấy đơn đặt sân", ErrorCodes.NotFound);

            // Validate branch access
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
        }

        // ── HELPERS ───────────────────────────────────────────────────────────
        private async Task ValidateBranchAccessAsync(
            Guid branchId, Guid userId, string userRole)
        {
            if (userRole == UserRole.OWNER.ToString()) return;

            var isInBranch = await _userBranchRepo.IsUserInBranchAsync(userId, branchId);
            if (!isInBranch)
                throw new AppException(403,
                    "Bạn không có quyền thao tác chi nhánh này", ErrorCodes.Forbidden);
        }

        private async Task<decimal> CalculateRefundPercentAsync(
            TimeOnly startTime, DateOnly bookingDate)
        {
            var bookingDateTime = bookingDate.ToDateTime(startTime);
            var hoursUntilStart = (bookingDateTime - DateTime.UtcNow).TotalHours;

            var policies = await _cancelPolicyRepo.GetAllAsync();
            var applicable = policies
                .Where(p => p.HoursBefore <= hoursUntilStart)
                .OrderByDescending(p => p.HoursBefore)
                .FirstOrDefault();

            return applicable?.RefundPercent ?? 0;
        }

        // ── EARN LOYALTY ──────────────────────────────────────────────────────
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

                // Check lên hạng — dùng ILoyaltyTierRepository đã được inject
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

                // Email lên hạng — lấy user qua IUserRepository
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

        private async Task SendConfirmationEmailAsync(
            Booking booking, Court court,
            TimeOnly startTime, TimeOnly endTime)
        {
            try
            {
                var email = booking.Customer?.Email ?? booking.GuestEmail;
                var name = booking.Customer?.FullName ?? booking.GuestName;
                if (string.IsNullOrEmpty(email)) return;

                // Tạo cancel token
                var rawToken = GenerateCancelToken();
                var tokenHash = HashToken(rawToken);
                var tokenExpiry = new DateTime[] {
                    booking.BookingDate.ToDateTime(startTime),
                    DateTime.UtcNow.AddHours(24)
                }.Min();

                booking.CancelTokenHash = tokenHash;
                booking.CancelTokenExpiresAt = tokenExpiry;
                await _bookingRepo.UpdateAsync(booking);

                await _emailService.SendBookingConfirmationAsync(
                    email, name!, booking.Id, rawToken,
                    court.Name, booking.BookingDate, startTime, endTime);
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
            BookingDate = b.BookingDate,
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
                StartTime = bc.StartTime,
                EndTime = bc.EndTime,
                PriceItems = bc.BookingPriceItems?.Select(bpi => new BookingPriceItemDto
                {
                    StartTime = bpi.TimeSlot?.StartTime ?? default,
                    EndTime = bpi.TimeSlot?.EndTime ?? default,
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
    }
}
