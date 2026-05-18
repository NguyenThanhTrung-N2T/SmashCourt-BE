using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class BookingRepository : IBookingRepository
    {
        private readonly SmashCourtContext _context;

        public BookingRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // Lấy danh sách booking với filter + phân quyền
        // Owner → thấy tất cả, Manager/Staff → chỉ thấy booking của chi nhánh mình
        private async Task<Guid?> ResolveScopedBranchIdAsync(Guid? requestedBranchId, string userRole, Guid userId)
        {
            if (userRole != UserRole.BRANCH_MANAGER.ToString() &&
                userRole != UserRole.STAFF.ToString())
            {
                return requestedBranchId;
            }

            return await _context.UserBranches
                .AsNoTracking()
                .Where(ub => ub.UserId == userId && ub.IsActive)
                .Select(ub => (Guid?)ub.BranchId)
                .FirstOrDefaultAsync();
        }

        public async Task<PagedResult<Booking>> GetAllAsync(
            BookingListQuery query, string userRole, Guid userId)
        {
            var q = _context.Bookings
                .AsNoTracking()
                .Include(b => b.Branch)
                .Include(b => b.Customer)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.Court)
                .Include(b => b.Invoice)
                .AsQueryable();

            // OWNER → thấy tất cả
            // MANAGER/STAFF → chỉ thấy chi nhánh mình
            var scopedBranchId = await ResolveScopedBranchIdAsync(query.BranchId, userRole, userId);
            if (scopedBranchId.HasValue)
                q = q.Where(b => b.BranchId == scopedBranchId.Value);

            // Filter
            if (query.Status.HasValue)
                q = q.Where(b => b.Status == query.Status);

            if (query.PaymentStatus.HasValue)
                q = q.Where(b => b.Invoice != null && b.Invoice.PaymentStatus == query.PaymentStatus);

            if (query.CourtId.HasValue)
                q = q.Where(b => b.BookingCourts.Any(bc => bc.CourtId == query.CourtId.Value));

            if (query.Date.HasValue)
                q = q.Where(b => b.BookingDate == DateOnly.FromDateTime(query.Date.Value));

            if (query.FromDate.HasValue)
                q = q.Where(b => b.BookingDate >= DateOnly.FromDateTime(query.FromDate.Value));

            if (query.ToDate.HasValue)
                q = q.Where(b => b.BookingDate <= DateOnly.FromDateTime(query.ToDate.Value));

            var keyword = query.CustomerKeyword;

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var search = keyword.Trim().ToLower();
                q = q.Where(b =>
                    (b.Customer != null && b.Customer.FullName.ToLower().Contains(search)) ||
                    (b.Customer != null && b.Customer.Phone != null && b.Customer.Phone.Contains(search)) ||
                    (b.GuestName != null && b.GuestName.ToLower().Contains(search)) ||
                    (b.GuestPhone != null && b.GuestPhone.Contains(search)) ||
                    b.Id.ToString().Contains(search));
            }

            var sortBy = query.SortBy?.Trim().ToLowerInvariant();
            var sortDesc = !string.Equals(query.SortOrder, "asc", StringComparison.OrdinalIgnoreCase);
            q = sortBy switch
            {
                "bookingdate" or "date" => sortDesc
                    ? q.OrderByDescending(b => b.BookingDate)
                    : q.OrderBy(b => b.BookingDate),
                "status" => sortDesc
                    ? q.OrderByDescending(b => b.Status)
                    : q.OrderBy(b => b.Status),
                "customername" or "customer" => sortDesc
                    ? q.OrderByDescending(b => b.Customer != null ? b.Customer.FullName : b.GuestName)
                    : q.OrderBy(b => b.Customer != null ? b.Customer.FullName : b.GuestName),
                "finaltotal" or "total" => sortDesc
                    ? q.OrderByDescending(b => b.Invoice != null ? b.Invoice.FinalTotal : 0)
                    : q.OrderBy(b => b.Invoice != null ? b.Invoice.FinalTotal : 0),
                _ => sortDesc
                    ? q.OrderByDescending(b => b.CreatedAt)
                    : q.OrderBy(b => b.CreatedAt)
            };

            var totalItems = await q.CountAsync();
            var items = await q
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Booking>
            {
                Items = items,
                TotalItems = totalItems,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }

        // Customer chỉ thấy booking của chính mình
        public async Task<List<BookingScheduleCourtDto>> GetScheduleAsync(
            BookingScheduleQuery query, string userRole, Guid userId)
        {
            var branchId = await ResolveScopedBranchIdAsync(query.BranchId, userRole, userId);
            if (!branchId.HasValue)
                throw new AppException(400, "Vui lòng chọn chi nhánh", ErrorCodes.BadRequest);

            var date = DateOnly.FromDateTime(query.Date);
            var activeStatuses = BookingStatusTransition.GetActiveStatuses();

            var courts = await _context.Courts
                .AsNoTracking()
                .Where(c => c.BranchId == branchId.Value && c.Status != CourtStatus.INACTIVE)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();

            var bookingCourts = await _context.BookingCourts
                .AsNoTracking()
                .Where(bc => bc.Court.BranchId == branchId.Value &&
                             bc.Date == date &&
                             bc.IsActive &&
                             activeStatuses.Contains(bc.Booking.Status))
                .Select(bc => new
                {
                    bc.CourtId,
                    bc.BookingId,
                    bc.StartTime,
                    bc.EndTime,
                    Status = bc.Booking.Status
                })
                .OrderBy(bc => bc.StartTime)
                .ToListAsync();

            var bookingsByCourt = bookingCourts
                .GroupBy(bc => bc.CourtId)
                .ToDictionary(g => g.Key, g => g.Select(bc => new BookingScheduleItemDto
                {
                    BookingId = bc.BookingId,
                    StartTime = bc.StartTime.ToString("HH:mm"),
                    EndTime = bc.EndTime.ToString("HH:mm"),
                    Status = bc.Status.ToString()
                }).ToList());

            return courts.Select(c => new BookingScheduleCourtDto
            {
                CourtId = c.Id,
                CourtName = c.Name,
                Bookings = bookingsByCourt.GetValueOrDefault(c.Id, [])
            }).ToList();
        }

        public async Task<BookingDashboardSummaryDto> GetDashboardSummaryAsync(
            BookingDashboardSummaryQuery query, string userRole, Guid userId)
        {
            var branchId = await ResolveScopedBranchIdAsync(query.BranchId, userRole, userId);
            var today = DateTimeHelper.GetTodayInVietnam();

            var bookings = _context.Bookings
                .AsNoTracking()
                .Where(b => b.BookingDate == today);

            if (branchId.HasValue)
                bookings = bookings.Where(b => b.BranchId == branchId.Value);

            var statusCounts = await bookings
                .GroupBy(b => b.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var activeStatuses = new[]
            {
                BookingStatus.IN_PROGRESS,
            };

            var todayRevenue = await _context.Invoices
                .AsNoTracking()
                .Where(i => i.Booking.BookingDate == today &&
                            i.PaymentStatus == InvoicePaymentStatus.PAID &&
                            i.Booking.Status != BookingStatus.CANCELLED &&
                            i.Booking.Status != BookingStatus.CANCELLED_PENDING_REFUND &&
                            i.Booking.Status != BookingStatus.CANCELLED_REFUNDED &&
                            i.Booking.Status != BookingStatus.NO_SHOW &&
                            (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
                .SumAsync(i => i.FinalTotal);

            var pendingRefunds = await _context.Refunds
                .AsNoTracking()
                .Where(r => r.Status == RefundStatus.PENDING &&
                            (!branchId.HasValue || r.Payment.Invoice.Booking.BranchId == branchId.Value))
                .CountAsync();

            return new BookingDashboardSummaryDto
            {
                TodayBookings = statusCounts.Sum(s => s.Count),
                ActiveBookings = statusCounts.Where(s => activeStatuses.Contains(s.Status)).Sum(s => s.Count),
                CompletedBookings = statusCounts.FirstOrDefault(s => s.Status == BookingStatus.COMPLETED)?.Count ?? 0,
                CancelledBookings = statusCounts
                    .Where(s => s.Status == BookingStatus.CANCELLED ||
                                s.Status == BookingStatus.CANCELLED_PENDING_REFUND ||
                                s.Status == BookingStatus.CANCELLED_REFUNDED)
                    .Sum(s => s.Count),
                TodayRevenue = todayRevenue,
                PendingRefunds = pendingRefunds
            };
        }

        public async Task<List<BookingCalendarHeatmapDto>> GetCalendarHeatmapAsync(
            BookingCalendarHeatmapQuery query, string userRole, Guid userId)
        {
            var branchId = await ResolveScopedBranchIdAsync(query.BranchId, userRole, userId);
            var startDate = new DateOnly(query.Year, query.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var branchesQuery = _context.Branches.AsNoTracking().AsQueryable();
            if (branchId.HasValue)
                branchesQuery = branchesQuery.Where(b => b.Id == branchId.Value);

            var branches = await branchesQuery
                .Select(b => new { b.Id, b.OpenTime, b.CloseTime })
                .ToListAsync();

            var branchIds = branches.Select(b => b.Id).ToList();

            var courtCounts = await _context.Courts
                .AsNoTracking()
                .Where(c => branchIds.Contains(c.BranchId) &&
                            c.Status != CourtStatus.INACTIVE &&
                            c.Status != CourtStatus.SUSPENDED)
                .GroupBy(c => c.BranchId)
                .Select(g => new { BranchId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BranchId, x => x.Count);

            var dailyAvailableHours = branches.Sum(b =>
                (decimal)((b.CloseTime - b.OpenTime).TotalHours * courtCounts.GetValueOrDefault(b.Id, 0)));

            var countedStatuses = new[]
            {
                BookingStatus.PENDING,
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE,
                BookingStatus.IN_PROGRESS,
                BookingStatus.PENDING_PAYMENT,
                BookingStatus.COMPLETED
            };

            var bookingCounts = await _context.Bookings
                .AsNoTracking()
                .Where(b => b.BookingDate >= startDate &&
                            b.BookingDate <= endDate &&
                            countedStatuses.Contains(b.Status) &&
                            (!branchId.HasValue || b.BranchId == branchId.Value))
                .GroupBy(b => b.BookingDate)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Date, x => x.Count);

            var bookedHours = await _context.BookingCourts
                .AsNoTracking()
                .Where(bc => bc.Date >= startDate &&
                             bc.Date <= endDate &&
                             countedStatuses.Contains(bc.Booking.Status) &&
                             (!branchId.HasValue || bc.Booking.BranchId == branchId.Value))
                .GroupBy(bc => bc.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Hours = g.Sum(bc => (decimal)(bc.EndTime - bc.StartTime).TotalHours)
                })
                .ToDictionaryAsync(x => x.Date, x => x.Hours);

            var revenue = await _context.Invoices
                .AsNoTracking()
                .Where(i => i.Booking.BookingDate >= startDate &&
                            i.Booking.BookingDate <= endDate &&
                            i.PaymentStatus == InvoicePaymentStatus.PAID &&
                            (!branchId.HasValue || i.Booking.BranchId == branchId.Value))
                .GroupBy(i => i.Booking.BookingDate)
                .Select(g => new { Date = g.Key, Revenue = g.Sum(i => i.FinalTotal) })
                .ToDictionaryAsync(x => x.Date, x => x.Revenue);

            var result = new List<BookingCalendarHeatmapDto>();
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var occupancyRate = dailyAvailableHours > 0
                    ? Math.Round(bookedHours.GetValueOrDefault(date, 0) / dailyAvailableHours, 2)
                    : 0;

                result.Add(new BookingCalendarHeatmapDto
                {
                    Date = date.ToString("yyyy-MM-dd"),
                    BookingCount = bookingCounts.GetValueOrDefault(date, 0),
                    OccupancyRate = occupancyRate,
                    Revenue = revenue.GetValueOrDefault(date, 0)
                });
            }

            return result;
        }

        public async Task<PagedResult<Booking>> GetByCustomerIdAsync(
            Guid customerId, BookingListQuery query)
        {
            var q = _context.Bookings
                .Include(b => b.Branch)
                .Include(b => b.Customer)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.Court)
                .Include(b => b.Invoice)
                .Where(b => b.CustomerId == customerId);

            // Apply filters
            if (query.BranchId.HasValue)
                q = q.Where(b => b.BranchId == query.BranchId.Value);

            if (query.Status.HasValue)
                q = q.Where(b => b.Status == query.Status.Value);

            if (query.Date.HasValue)
            {
                var date = DateOnly.FromDateTime(query.Date.Value);
                q = q.Where(b => b.BookingDate == date);
            }

            if (!string.IsNullOrWhiteSpace(query.CustomerKeyword))
            {
                var search = query.CustomerKeyword.Trim().ToLower();
                q = q.Where(b =>
                    (b.Customer != null && b.Customer.FullName.ToLower().Contains(search)) ||
                    (b.Customer != null && b.Customer.Phone != null && b.Customer.Phone.Contains(search)) ||
                    (b.GuestName != null && b.GuestName.ToLower().Contains(search)) ||
                    (b.GuestPhone != null && b.GuestPhone.Contains(search)) ||
                    b.Id.ToString().Contains(search)
                );
            }

            q = q.OrderByDescending(b => b.CreatedAt);

            var totalItems = await q.CountAsync();
            var items = await q
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Booking>
            {
                Items = items,
                TotalItems = totalItems,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }

        // Lấy thông tin booking theo id, có phân quyền
        public async Task<Booking?> GetByIdWithDetailsAsync(Guid id)
        {
            return await _context.Bookings
                .Include(b => b.Branch)
                .Include(b => b.Customer)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.Court)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.BookingPriceItems)
                        .ThenInclude(bpi => bpi.TimeSlot)
                .Include(b => b.BookingServices)
                .Include(b => b.Invoice)
                    .ThenInclude(i => i!.Payments)   // cần để tạo refund record khi hủy
                .Include(b => b.BookingPromotion)
                .FirstOrDefaultAsync(b => b.Id == id);
        }

        /// <summary>
        /// Lấy booking status (lightweight query - chỉ lấy status)
        /// Dùng để re-check status trong transaction, tránh race condition
        /// </summary>
        /// <param name="id">Booking ID</param>
        /// <returns>BookingStatus hiện tại</returns>
        public async Task<BookingStatus> GetBookingStatusAsync(Guid id)
        {
            return await _context.Bookings
                .Where(b => b.Id == id)
                .Select(b => b.Status)
                .FirstOrDefaultAsync();
        }


        // Lấy thông tin booking theo token hủy (dùng cho khách hàng hủy booking online)
        public async Task<Booking?> GetByCancelTokenAsync(string tokenHash)
        {
            return await _context.Bookings
                .Include(b => b.Branch)
                .Include(b => b.Customer)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.Court)
                .Include(b => b.Invoice)
                    .ThenInclude(i => i!.Payments)   // cần để tạo refund record khi hủy
                .FirstOrDefaultAsync(b => b.CancelTokenHash == tokenHash);
        }

        // Check slot có bị đặt chưa — check booking_courts active VÀ booking status active
        public async Task<bool> HasOverlapAsync(
            Guid courtId, DateOnly date,
            TimeOnly startTime, TimeOnly endTime)
        {
            // Lấy danh sách status active từ BookingStatusTransition helper
            var activeStatuses = BookingStatusTransition.GetActiveStatuses();

            return await _context.BookingCourts
                .Where(bc =>
                    bc.CourtId == courtId &&
                    bc.Date == date &&
                    bc.IsActive &&
                    activeStatuses.Contains(bc.Booking.Status) &&  // Chỉ tính booking đang active
                    bc.StartTime < endTime &&
                    bc.EndTime > startTime)
                .AnyAsync();
        }

        // Batch load tất cả BookingCourt active cho court trong ngày — dùng cho TimeGrid
        public async Task<List<BookingCourt>> GetActiveByCourtAndDateAsync(
            Guid courtId, DateOnly date)
        {
            // Source of truth: Booking.Status (không phụ thuộc IsActive)
            // IsActive là derived state → có thể bị bug khi update
            // Chỉ lọc theo status thực sự chiếm sân
            var validStatuses = new[]
            {
                BookingStatus.PENDING,
                BookingStatus.CONFIRMED,
                BookingStatus.PAID_ONLINE,
                BookingStatus.IN_PROGRESS
            };

            return await _context.BookingCourts
                .Include(bc => bc.Booking)
                .Where(bc =>
                    bc.CourtId == courtId &&
                    bc.Date == date &&
                    validStatuses.Contains(bc.Booking.Status))  // ✅ Chỉ dựa vào Status
                .ToListAsync();
        }

        // tạo mới booking
        public async Task<Booking> CreateAsync(Booking booking)
        {
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();
            return booking;
        }

        // cập nhật booking
        public async Task UpdateAsync(Booking booking)
        {
            _context.Bookings.Update(booking);
            await _context.SaveChangesAsync();
        }

        // Thêm mới booking court
        public async Task<BookingCourt> AddCourtAsync(BookingCourt bookingCourt)
        {
            _context.BookingCourts.Add(bookingCourt);
            await _context.SaveChangesAsync();
            return bookingCourt; // Id đã được gen
        }

        // thêm nhiều booking price item cùng lúc
        public async Task AddPriceItemsAsync(List<BookingPriceItem> items)
        {
            _context.BookingPriceItems.AddRange(items);
            await _context.SaveChangesAsync();
        }

        // Thêm promotion vào booking
        public async Task AddPromotionAsync(BookingPromotion promotion)
        {
            _context.BookingPromotions.Add(promotion);
            await _context.SaveChangesAsync();
        }

        // Thêm dịch vụ vào booking
        public async Task AddServiceAsync(BookingService service)
        {
            _context.BookingServices.Add(service);
            await _context.SaveChangesAsync();
        }

        // Cập nhật dịch vụ trong booking
        public async Task UpdateServiceAsync(BookingService service)
        {
            _context.BookingServices.Update(service);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Cập nhật quantity của service một cách atomic để tránh race condition
        /// Sử dụng ExecuteUpdateAsync với SET quantity = quantity + @delta
        /// </summary>
        /// <param name="serviceId">ID của booking service</param>
        /// <param name="quantityToAdd">Số lượng cần thêm (có thể âm để trừ)</param>
        /// <returns>Quantity mới sau khi update</returns>
        /// <remarks>
        /// Race condition protection:
        /// - Thread A và B cùng đọc quantity = 1
        /// - Nếu dùng read-modify-write: cả 2 đều update thành 2 (lost update)
        /// - Dùng atomic update: UPDATE SET quantity = quantity + @delta
        /// - DB đảm bảo serializable → không bị lost update
        /// </remarks>
        public async Task<int> UpdateServiceQuantityAtomicAsync(Guid serviceId, int quantityToAdd)
        {
            // Atomic update: UPDATE booking_services SET quantity = quantity + @quantityToAdd
            // WHERE id = @serviceId
            await _context.BookingServices
                .Where(s => s.Id == serviceId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Quantity, x => x.Quantity + quantityToAdd));

            // Đọc lại giá trị mới để return
            var newQuantity = await _context.BookingServices
                .Where(s => s.Id == serviceId)
                .Select(s => s.Quantity)
                .FirstOrDefaultAsync();

            return newQuantity;
        }

        // Tính tổng service fee của booking (query từ DB, không dùng memory)
        public async Task<decimal> CalculateServiceFeeAsync(Guid bookingId)
        {
            return await _context.BookingServices
                .Where(s => s.BookingId == bookingId)
                .SumAsync(s => s.UnitPrice * s.Quantity);
        }

        // Xóa dịch vụ khỏi booking
        public async Task RemoveServiceAsync(BookingService service)
        {
            _context.BookingServices.Remove(service);
            await _context.SaveChangesAsync();
        }

        // Cập nhật trạng thái active của booking court (dùng để check-in/check-out)
        public async Task UpdateCourtActiveStatusAsync(Guid bookingId, bool isActive)
        {
            await _context.BookingCourts
                .Where(bc => bc.BookingId == bookingId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(bc => bc.IsActive, isActive));
        }

        /// <summary>
        /// Atomic consume cancel token để tránh race condition khi hủy booking qua link
        /// Sử dụng WHERE condition để đảm bảo chỉ 1 request thành công (first-come-first-served)
        /// </summary>
        /// <param name="bookingId">Booking ID</param>
        /// <param name="tokenHash">Token hash (SHA256)</param>
        /// <param name="consumedAt">Thời gian consume token</param>
        /// <returns>true nếu consume thành công, false nếu token đã được dùng</returns>
        /// <remarks>
        /// Race condition protection:
        /// - User 1 và User 2 click cùng link → cả 2 gọi TryConsumeTokenAsync
        /// - WHERE condition: CancelTokenUsedAt == null
        /// - Chỉ 1 request thắng (rowsAffected = 1), request kia thua (rowsAffected = 0)
        /// - Request thua sẽ nhận "Link đã được sử dụng"
        /// </remarks>
        public async Task<bool> TryConsumeTokenAsync(Guid bookingId, string tokenHash, DateTime consumedAt)
        {
            // ExecuteUpdateAsync với WHERE condition = Atomic operation
            // UPDATE bookings SET cancel_token_used_at = @consumedAt
            // WHERE id = @bookingId AND cancel_token_hash = @tokenHash AND cancel_token_used_at IS NULL
            var rowsAffected = await _context.Bookings
                .Where(b => b.Id == bookingId &&
                           b.CancelTokenHash == tokenHash &&
                           b.CancelTokenUsedAt == null)  // Chỉ update nếu chưa dùng
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(b => b.CancelTokenUsedAt, consumedAt));

            // rowsAffected = 1 → thành công (token chưa dùng)
            // rowsAffected = 0 → thất bại (token đã dùng hoặc không tồn tại)
            return rowsAffected > 0;
        }

        /// <summary>
        /// Cập nhật booking status với conditional update để tránh race condition
        /// Sử dụng WHERE condition để đảm bảo chỉ update nếu status vẫn đúng như mong đợi
        /// </summary>
        /// <param name="bookingId">ID của booking</param>
        /// <param name="newStatus">Status mới</param>
        /// <param name="expectedStatus">Status mong đợi (status cũ trước khi update)</param>
        /// <returns>Số rows affected (1 = thành công, 0 = conflict)</returns>
        /// <remarks>
        /// Race condition protection cho checkout:
        /// - Staff A và Staff B cùng checkout 1 booking
        /// - WHERE condition: status = expectedStatus (IN_PROGRESS hoặc PENDING_PAYMENT)
        /// - Chỉ 1 request thắng (rowsAffected = 1), request kia thua (rowsAffected = 0)
        /// - Request thua sẽ nhận "Đơn đã được checkout bởi người khác"
        /// 
        /// DB-level atomic operation:
        /// UPDATE bookings SET status = @newStatus, updated_at = NOW()
        /// WHERE id = @bookingId AND status = @expectedStatus
        /// </remarks>
        public async Task<int> UpdateWithStatusCheckAsync(
            Guid bookingId,
            BookingStatus newStatus,
            BookingStatus expectedStatus)
        {
            // ExecuteUpdateAsync với WHERE condition = Atomic operation
            // Pass giá trị trực tiếp, KHÔNG dùng entity tracking
            var rowsAffected = await _context.Bookings
                .Where(b => b.Id == bookingId && b.Status == expectedStatus)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, newStatus)  // ← Pass giá trị trực tiếp
                    .SetProperty(b => b.UpdatedAt, DateTime.UtcNow));

            // rowsAffected = 1 → thành công (status đúng như mong đợi)
            // rowsAffected = 0 → conflict (status đã thay đổi bởi request khác)
            return rowsAffected;
        }

        /// <summary>
        /// Atomic update booking khi thanh toán thành công (VNPay IPN)
        /// Update TẤT CẢ fields trong 1 operation duy nhất để tránh race condition
        /// </summary>
        /// <param name="bookingId">ID của booking</param>
        /// <param name="expectedStatus">Status mong đợi (PENDING)</param>
        /// <param name="cancelTokenHash">Cancel token hash</param>
        /// <param name="cancelTokenExpiry">Cancel token expiry</param>
        /// <param name="now">Timestamp hiện tại</param>
        /// <returns>Số rows affected (1 = thành công, 0 = conflict)</returns>
        /// <remarks>
        /// Race condition protection cho VNPay payment:
        /// - IPN và Confirm có thể gọi cùng lúc
        /// - WHERE condition: status = PENDING
        /// - Chỉ 1 request thắng (rowsAffected = 1), request kia thua (rowsAffected = 0)
        /// 
        /// DB-level atomic operation (1 UPDATE statement duy nhất):
        /// UPDATE bookings 
        /// SET status = 'PAID_ONLINE',
        ///     expires_at = NULL,
        ///     cancel_token_hash = @hash,
        ///     cancel_token_expires_at = @expiry,
        ///     updated_at = @now
        /// WHERE id = @bookingId AND status = @expectedStatus
        /// </remarks>
        public async Task<int> AtomicUpdatePaymentSuccessAsync(
            Guid bookingId,
            BookingStatus expectedStatus,
            string cancelTokenHash,
            DateTime cancelTokenExpiry,
            DateTime now)
        {
            // ATOMIC: Tất cả updates trong 1 ExecuteUpdateAsync duy nhất
            var rowsAffected = await _context.Bookings
                .Where(b => b.Id == bookingId && b.Status == expectedStatus)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Status, BookingStatus.PAID_ONLINE)
                    .SetProperty(b => b.ExpiresAt, (DateTime?)null)
                    .SetProperty(b => b.CancelTokenHash, cancelTokenHash)
                    .SetProperty(b => b.CancelTokenExpiresAt, cancelTokenExpiry)
                    .SetProperty(b => b.UpdatedAt, now));

            return rowsAffected;
        }

        public async Task<int> GetCompletedBookingCountAsync(Guid customerId)
        {
            return await _context.Bookings
                .Where(b => b.CustomerId == customerId && b.Status == BookingStatus.COMPLETED)
                .CountAsync();
        }
    }
}
