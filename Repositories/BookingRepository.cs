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
        public async Task<PagedResult<Booking>> GetAllAsync(
            BookingListQuery query, string userRole, Guid userId)
        {
            var q = _context.Bookings
                .Include(b => b.Branch)
                .Include(b => b.Customer)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.Court)
                .Include(b => b.Invoice)
                .AsQueryable();

            // OWNER → thấy tất cả
            // MANAGER/STAFF → chỉ thấy chi nhánh mình
            if (userRole == UserRole.BRANCH_MANAGER.ToString() ||
                userRole == UserRole.STAFF.ToString())
            {
                var branchId = await _context.UserBranches
                    .Where(ub => ub.UserId == userId && ub.IsActive)
                    .Select(ub => ub.BranchId)
                    .FirstOrDefaultAsync();

                q = q.Where(b => b.BranchId == branchId);
            }

            // Filter
            if (query.BranchId.HasValue)
                q = q.Where(b => b.BranchId == query.BranchId);

            if (query.Status.HasValue)
                q = q.Where(b => b.Status == query.Status);

            if (query.Date.HasValue)
                q = q.Where(b => b.BookingDate == DateOnly.FromDateTime(query.Date.Value));

            if (!string.IsNullOrEmpty(query.Search))
            {
                var search = query.Search.ToLower();
                q = q.Where(b =>
                    (b.Customer != null && b.Customer.FullName.ToLower().Contains(search)) ||
                    (b.Customer != null && b.Customer.Phone != null && b.Customer.Phone.Contains(search)) ||
                    (b.GuestName != null && b.GuestName.ToLower().Contains(search)) ||
                    (b.GuestPhone != null && b.GuestPhone.Contains(search)) ||
                    b.Id.ToString().Contains(search));
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

        // Customer chỉ thấy booking của chính mình
        public async Task<PagedResult<Booking>> GetByCustomerIdAsync(
            Guid customerId, PaginationQuery query)
        {
            var q = _context.Bookings
                .Include(b => b.Branch)
                .Include(b => b.Customer)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.Court)
                .Include(b => b.Invoice)
                .Where(b => b.CustomerId == customerId)
                .OrderByDescending(b => b.CreatedAt);

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
            // ✅ ATOMIC: Tất cả updates trong 1 ExecuteUpdateAsync duy nhất
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
    }
}
