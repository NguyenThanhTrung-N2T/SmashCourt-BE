using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Repositories
{
    public class CancelPolicyRepository : ICancelPolicyRepository
    {
        private readonly SmashCourtContext _db;

        public CancelPolicyRepository(SmashCourtContext db)
        {
            _db = db;
        }

        // Lấy tất cả chính sách hủy, sắp xếp theo HoursBefore tăng dần
        public async Task<IEnumerable<CancelPolicy>> GetAllAsync()
        {
            return await _db.CancelPolicies
                .OrderBy(p => p.HoursBefore)
                .ToListAsync();
        }

        // Lấy chính sách hủy theo Id
        public async Task<CancelPolicy?> GetByIdAsync(Guid id)
        {
            return await _db.CancelPolicies.FindAsync(id);
        }

        // Lấy chính sách hủy theo số giờ trước khi đặt sân, trả về null nếu không tìm thấy
        public async Task<CancelPolicy?> GetByHoursBeforeAsync(int hoursBefore)
        {
            return await _db.CancelPolicies
                .FirstOrDefaultAsync(p => p.HoursBefore == hoursBefore);
        }

        // Tạo mới một chính sách hủy
        public async Task CreateAsync(CancelPolicy policy)
        {
            await _db.CancelPolicies.AddAsync(policy);
            await _db.SaveChangesAsync();
        }

        // Cập nhật một chính sách hủy
        public async Task UpdateAsync(CancelPolicy policy)
        {
            _db.CancelPolicies.Update(policy);
            await _db.SaveChangesAsync();
        }

        // Xóa một chính sách hủy
        public async Task DeleteAsync(CancelPolicy policy)
        {
            _db.CancelPolicies.Remove(policy);
            await _db.SaveChangesAsync();
        }
    }
}
