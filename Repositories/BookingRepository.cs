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
            return await _context.BookingCourts
                .Where(bc =>
                    bc.CourtId == courtId &&
                    bc.Date == date &&
                    bc.IsActive)
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

    }
}
