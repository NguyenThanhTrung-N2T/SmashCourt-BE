using SmashCourt_BE.Common;
using SmashCourt_BE.Data;
using SmashCourt_BE.DTOs.Booking;
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
                q = q.Where(b => b.BookingDate == query.Date);

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

        public async Task<Booking?> GetByIdAsync(Guid id)
        {
            return await _context.Bookings.FindAsync(id);
        }

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
                .Include(b => b.BookingPromotion)
                .FirstOrDefaultAsync(b => b.Id == id);
        }

        public async Task<Booking?> GetByCancelTokenAsync(string tokenHash)
        {
            return await _context.Bookings
                .Include(b => b.Branch)
                .Include(b => b.Customer)
                .Include(b => b.BookingCourts)
                    .ThenInclude(bc => bc.Court)
                .Include(b => b.Invoice)
                .FirstOrDefaultAsync(b => b.CancelTokenHash == tokenHash);
        }

        // Check slot có bị đặt chưa — check booking_courts active
        public async Task<bool> HasOverlapAsync(
            Guid courtId, DateOnly date,
            TimeOnly startTime, TimeOnly endTime)
        {
            return await _context.BookingCourts
                .Where(bc =>
                    bc.CourtId == courtId &&
                    bc.Date == date &&
                    bc.IsActive &&
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

        public async Task<Booking> CreateAsync(Booking booking)
        {
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();
            return booking;
        }

        public async Task UpdateAsync(Booking booking)
        {
            _context.Bookings.Update(booking);
            await _context.SaveChangesAsync();
        }

        public async Task<BookingCourt> AddCourtAsync(BookingCourt bookingCourt)
        {
            _context.BookingCourts.Add(bookingCourt);
            await _context.SaveChangesAsync();
            return bookingCourt; // Id đã được gen
        }

        public async Task AddPriceItemsAsync(List<BookingPriceItem> items)
        {
            _context.BookingPriceItems.AddRange(items);
            await _context.SaveChangesAsync();
        }

        public async Task AddPromotionAsync(BookingPromotion promotion)
        {
            _context.BookingPromotions.Add(promotion);
            await _context.SaveChangesAsync();
        }

        public async Task AddServiceAsync(BookingService service)
        {
            _context.BookingServices.Add(service);
            await _context.SaveChangesAsync();
        }

        public async Task RemoveServiceAsync(BookingService service)
        {
            _context.BookingServices.Remove(service);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCourtActiveStatusAsync(Guid bookingId, bool isActive)
        {
            await _context.BookingCourts
                .Where(bc => bc.BookingId == bookingId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(bc => bc.IsActive, isActive));
        }

    }
}
