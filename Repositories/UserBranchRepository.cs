using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;
using Microsoft.EntityFrameworkCore;
using SmashCourt_BE.Models.Enums;
namespace SmashCourt_BE.Repositories
{
    public class UserBranchRepository : IUserBranchRepository
    {
        private readonly SmashCourtContext _context;

        public UserBranchRepository(SmashCourtContext context)
        {
            _context = context;
        }

        // Lấy thông tin gán chi nhánh của user đang hoạt động
        public async Task<UserBranch?> GetActiveByUserIdAsync(Guid userId)
        {
            return await _context.UserBranches
                .FirstOrDefaultAsync(ub =>
                    ub.UserId == userId &&
                    ub.IsActive);
        }

        // Lấy manager active của chi nhánh
        public async Task<UserBranch?> GetActiveManagerByBranchIdAsync(Guid branchId)
        {
            return await _context.UserBranches
                .FirstOrDefaultAsync(ub =>
                    ub.BranchId == branchId &&
                    ub.Role == UserBranchRole.MANAGER &&
                    ub.IsActive);
        }

        // tạo mới gán chi nhánh cho user
        public async Task<UserBranch> CreateAsync(UserBranch userBranch)
        {
            _context.UserBranches.Add(userBranch);
            await _context.SaveChangesAsync();
            return userBranch;
        }

        // cập nhật thông tin gán chi nhánh của user
        public async Task UpdateAsync(UserBranch userBranch)
        {
            _context.UserBranches.Update(userBranch);
            await _context.SaveChangesAsync();
        }

        // kiểm tra xem user có đang gán vào chi nhánh nào không — dùng để xác thực quyền truy cập hoặc hiển thị thông tin chi nhánh của user
        public async Task<bool> IsUserInBranchAsync(Guid userId, Guid branchId)
        {
            return await _context.UserBranches
                .AnyAsync(ub =>
                    ub.UserId == userId &&
                    ub.BranchId == branchId &&
                    ub.IsActive);
        }
    }
}
