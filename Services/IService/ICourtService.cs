using SmashCourt_BE.DTOs.Court;

namespace SmashCourt_BE.Services.IService
{
    public interface ICourtService
    {
        // STAFF / ADMIN → thấy tất cả sân đang hoạt động + sân bị khóa + sân bị đặt + sân đang sử dụng
        Task<List<CourtDto>> GetAllByBranchAsync(Guid branchId, bool isStaffOrAbove);

        // lấy sân theo id, nếu là staff/admin thì thấy tất cả sân, nếu là khách hàng thì chỉ thấy sân đang hoạt động
        Task<CourtDto> GetByIdAsync(Guid id, Guid branchId, bool isStaffOrAbove);

        // chỉ OWNER / MANAGER mới có quyền tạo sân
        Task<CourtDto> CreateAsync(Guid branchId, CreateCourtDto dto, Guid currentUserId, string currentUserRole);

        // chỉ OWNER / MANAGER mới có quyền cập nhật sân
        Task<CourtDto> UpdateAsync(Guid id, Guid branchId, UpdateCourtDto dto, Guid currentUserId, string currentUserRole);
        Task SuspendAsync(Guid id, Guid branchId, Guid currentUserId, string currentUserRole);
        Task ActivateAsync(Guid id, Guid branchId, Guid currentUserId, string currentUserRole);
        Task DeleteAsync(Guid id, Guid branchId, Guid currentUserId, string currentUserRole);
    }
}
