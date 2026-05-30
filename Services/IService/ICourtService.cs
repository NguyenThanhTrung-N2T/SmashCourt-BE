using SmashCourt_BE.DTOs.Court;

namespace SmashCourt_BE.Services.IService
{
    public interface ICourtService
    {
        /// <summary>
        /// Lấy tất cả sân (dùng cho cả Public/Customer và Manager/Staff)
        /// Phân biệt logic qua role và branch auto-resolve.
        /// </summary>
        Task<List<CourtDto>> GetAllAsync(
            Guid? requestedBranchId, Guid? typeId, 
            Guid? currentUserId, string? currentUserRole);

        /// <summary>
        /// Lấy chi tiết 1 sân
        /// </summary>
        Task<CourtDto> GetByIdAsync(Guid id, Guid? requestedBranchId, Guid? currentUserId, string? currentUserRole);

        /// <summary>
        /// Tạo sân mới (OWNER / MANAGER)
        /// </summary>
        Task<CourtDto> CreateAsync(Guid? requestedBranchId, CreateCourtDto dto, Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Cập nhật sân (OWNER / MANAGER)
        /// </summary>
        Task<CourtDto> UpdateAsync(Guid id, Guid? requestedBranchId, UpdateCourtDto dto, Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Tạm ngưng sân
        /// </summary>
        Task SuspendAsync(Guid id, Guid? requestedBranchId, Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Kích hoạt lại sân
        /// </summary>
        Task ActivateAsync(Guid id, Guid? requestedBranchId, Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Xóa mềm sân
        /// </summary>
        Task DeleteAsync(Guid id, Guid? requestedBranchId, Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Stats-only dashboard (4 ô thống kê) — có thể poll độc lập mọi 30–60 giây.
        /// </summary>
        Task<CourtManagementStatsDto> GetManagementStatsAsync(
            Guid? requestedBranchId, DateOnly? date,
            Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Danh sách card sân (phân trang) kèm timeline ngày được chọn.
        /// </summary>
        Task<Common.PagedResult<CourtManagementCardDto>> GetManagementCourtsAsync(
            Guid? requestedBranchId, DateOnly? date, string? search, Guid? typeId,
            int page, int pageSize,
            Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Full-detail timeline cho mọt ngày — có booking identity để vẽ named blocks.
        /// </summary>
        Task<CourtManagementTimelineDto> GetManagementTimelineAsync(
            Guid? requestedBranchId, DateOnly date, Guid? typeId,
            Guid currentUserId, string currentUserRole);

        /// <summary>
        /// Chi tiết sân cho modal quản lý (date-scoped)
        /// </summary>
        Task<CourtManagementDetailDto> GetManagementDetailAsync(
            Guid id, DateOnly? date, Guid currentUserId, string currentUserRole);
    }
}
