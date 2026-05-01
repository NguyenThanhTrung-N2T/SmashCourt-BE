using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Repositories.IRepository;
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
            Guid branchId, bool isStaffOrAbove)
        {
            var query = _context.Courts
                .Include(c => c.CourtType)
                .Where(c =>
                    c.BranchId == branchId &&
                    c.Status != CourtStatus.INACTIVE);

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
    }
}
