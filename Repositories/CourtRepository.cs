using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace SmashCourt_BE.Repositories
{
    public class CourtRepository : ICourtRepository
    {
        private readonly SmashCourtContext _context;

        public CourtRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // STAFF / ADMIN → thấy tất cả sân đang hoạt động + sân bị khóa + sân bị đặt + sân đang sử dụng
        public async Task<List<Court>> GetAllByBranchAsync(
            Guid branchId, bool isStaffOrAbove, Guid? courtTypeId = null)
        {
            var query = _context.Courts
                .Include(c => c.CourtType)
                .Where(c =>
                    c.BranchId == branchId &&
                    c.Status != CourtStatus.INACTIVE);

            if (courtTypeId.HasValue)
            {
                query = query.Where(c => c.CourtTypeId == courtTypeId.Value);
            }

            // CUSTOMER / Public → chỉ thấy sân đang hoạt động
            if (!isStaffOrAbove)
                query = query.Where(c =>
                    c.Status == CourtStatus.AVAILABLE ||
                    c.Status == CourtStatus.LOCKED ||
                    c.Status == CourtStatus.IN_USE);

            return await query
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        // lấy thông tin sân theo id
        // nếu branchId được truyền vào thì chỉ lấy sân thuộc chi nhánh đó (bảo mật cho staff)
        // nếu branchId là null thì lấy theo id đơn thuần, không lọc branch (dùng trong booking khi chưa biết branchId)
        public async Task<Court?> GetByIdAsync(Guid id, Guid? branchId = null)
        {
            return await _context.Courts
                .Include(c => c.CourtType)
                .FirstOrDefaultAsync(c =>
                    c.Id == id &&
                    (branchId == null || c.BranchId == branchId) &&
                    c.Status != CourtStatus.INACTIVE);
        }

        // lấy danh sách sân theo id, bỏ qua các sân đã bị xóa (INACTIVE)
        public async Task<List<Court>> GetByIdsAsync(IEnumerable<Guid> ids)
        {
            return await _context.Courts
                .Include(c => c.CourtType)
                .Where(c => ids.Contains(c.Id) && c.Status != CourtStatus.INACTIVE)
                .ToListAsync();
        }

        // kiểm tra tên sân đã tồn tại trong chi nhánh hay chưa, chỉ kiểm tra sân đang hoạt động + sân bị khóa + sân bị đặt + sân đang sử dụng
        public async Task<bool> ExistsByNameAsync(
            string name, Guid branchId, Guid? excludeId = null)
        {
            return await _context.Courts
                .Where(c =>
                    c.BranchId == branchId &&
                    c.Status != CourtStatus.INACTIVE &&
                    c.Name.ToLower() == name.ToLower() &&
                    (excludeId == null || c.Id != excludeId))
                .AnyAsync();
        }

        // kiểm tra sân có đang được đặt hay không, chỉ kiểm tra các booking có trạng thái đang hoạt động
        public async Task<bool> HasActiveBookingsAsync(Guid courtId)
        {
            var activeStatuses = new[]
            {
            BookingStatus.PENDING,
            BookingStatus.CONFIRMED,
            BookingStatus.PAID_ONLINE,
            BookingStatus.IN_PROGRESS
        };

            return await _context.BookingCourts
                .AnyAsync(bc =>
                    bc.CourtId == courtId &&
                    bc.IsActive &&
                    activeStatuses.Contains(bc.Booking.Status));
        }


        // STAFF / ADMIN → có thể tạo sân mới
        public async Task<Court> CreateAsync(Court court)
        {
            _context.Courts.Add(court);
            await _context.SaveChangesAsync();
            return court;
        }

        // STAFF / ADMIN → có thể cập nhật thông tin sân
        public async Task UpdateAsync(Court court)
        {
            _context.Courts.Update(court);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Batch update trạng thái nhiều sân cùng lúc (tránh N+1 query)
        /// Dùng ExecuteUpdateAsync để update trực tiếp trong DB mà không cần load entities vào memory
        /// </summary>
        /// <param name="courtIds">Danh sách court IDs cần update</param>
        /// <param name="status">Status mới (AVAILABLE, BOOKED, IN_USE, SUSPENDED)</param>
        /// <param name="updatedAt">Thời gian update</param>
        public async Task BatchUpdateStatusAsync(List<Guid> courtIds, CourtStatus status, DateTime updatedAt)
        {
            if (!courtIds.Any()) return;

            // ExecuteUpdateAsync: Bulk update trực tiếp trong DB (không load entities)
            // Performance: O(1) query thay vì O(N) queries
            await _context.Courts
                .Where(c => courtIds.Contains(c.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.UpdatedAt, updatedAt));
        }
        /// <summary>
        /// Bulk-fetch tất cả dữ liệu cần thiết cho management dashboard trong 3 queries.
        /// Courts + Branch (1 query), BookingCourts for the given date (1 query), filter in-memory.
        /// </summary>
        public async Task<CourtManagementBulkData> GetManagementDashboardDataAsync(
            Guid branchId, DateOnly date, string? search, Guid? typeId)
        {
            // Query 1: Branch info
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == branchId)
                ?? throw new Common.AppException(404, "Không tìm thấy chi nhánh", Common.ErrorCodes.NotFound);

            // Query 2: All active courts for the branch (with CourtType for name/price lookup)
            var courtQuery = _context.Courts
                .AsNoTracking()
                .Include(c => c.CourtType)
                .Where(c => c.BranchId == branchId && c.Status != CourtStatus.INACTIVE);

            if (!string.IsNullOrWhiteSpace(search))
                courtQuery = courtQuery.Where(c => c.Name.ToLower().Contains(search.Trim().ToLower()));

            if (typeId.HasValue)
                courtQuery = courtQuery.Where(c => c.CourtTypeId == typeId.Value);

            var courts = await courtQuery.OrderBy(c => c.Name).ToListAsync();

            // Query 3: All active BookingCourts for the given date for courts in this branch
            var courtIds = courts.Select(c => c.Id).ToList();
            var activeStatuses = GetTimelineStatuses();

            var bookingCourts = await _context.BookingCourts
                .AsNoTracking()
                .Include(bc => bc.Booking)
                    .ThenInclude(b => b.Customer)
                .Where(bc =>
                    courtIds.Contains(bc.CourtId) &&
                    bc.Date == date &&
                    // bc.IsActive &&
                    activeStatuses.Contains(bc.Booking.Status))
                .OrderBy(bc => bc.StartTime)
                .ToListAsync();

            return new CourtManagementBulkData
            {
                Branch = branch,
                Courts = courts,
                TodayBookingCourts = bookingCourts
            };
        }

        /// <summary>
        /// Bulk-fetch dữ liệu cho management-timeline: giống dashboard nhng không có search filter và luôn
        /// include booking identity (Customer name, status) để vẽ named blocks.
        /// </summary>
        public async Task<CourtManagementBulkData> GetManagementTimelineDataAsync(
            Guid branchId, DateOnly date, Guid? typeId)
        {
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == branchId)
                ?? throw new Common.AppException(404, "Không tìm thấy chi nhánh", Common.ErrorCodes.NotFound);

            var courtQuery = _context.Courts
                .AsNoTracking()
                .Include(c => c.CourtType)
                .Where(c => c.BranchId == branchId && c.Status != CourtStatus.INACTIVE);

            if (typeId.HasValue)
                courtQuery = courtQuery.Where(c => c.CourtTypeId == typeId.Value);

            var courts = await courtQuery.OrderBy(c => c.Name).ToListAsync();
            var courtIds = courts.Select(c => c.Id).ToList();

            var activeStatuses = GetTimelineStatuses();

            var bookingCourts = await _context.BookingCourts
                .AsNoTracking()
                .Include(bc => bc.Booking)
                    .ThenInclude(b => b.Customer)
                .Where(bc =>
                    courtIds.Contains(bc.CourtId) &&
                    bc.Date == date &&
                    // bc.IsActive &&
                    activeStatuses.Contains(bc.Booking.Status))
                .OrderBy(bc => bc.StartTime)
                .ToListAsync();

            return new CourtManagementBulkData
            {
                Branch = branch,
                Courts = courts,
                TodayBookingCourts = bookingCourts
            };
        }
        private static BookingStatus[] GetTimelineStatuses() =>
        [
            BookingStatus.PENDING,
            BookingStatus.CONFIRMED,
            BookingStatus.PAID_ONLINE,
            BookingStatus.PENDING_PAYMENT,
            BookingStatus.IN_PROGRESS,
            BookingStatus.COMPLETED,
        ];
    }
}
