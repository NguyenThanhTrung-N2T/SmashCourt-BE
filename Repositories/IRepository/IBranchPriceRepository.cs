using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBranchPriceRepository
    {
        // Lấy tất cả cấu hình giá override của chi nhánh, có thể filter theo courtTypeId
        Task<List<BranchPriceOverride>> GetAllAsync(Guid branchId, Guid? courtTypeId = null);

        // Lấy cấu hình giá override hiện tại (có hiệu lực vào thời điểm hiện tại) của chi nhánh, có thể filter theo courtTypeId
        Task<List<BranchPriceOverride>> GetCurrentAsync(Guid branchId, Guid? courtTypeId = null);

        // Lấy cấu hình giá override có hiệu lực vào một ngày cụ thể của chi nhánh, có thể filter theo courtTypeId
        Task<List<BranchPriceOverride>> GetCurrentForDateAsync(Guid branchId, DateOnly targetDate, Guid? courtTypeId = null);

        // Kiểm tra xem đã tồn tại cấu hình giá override nào cho chi nhánh, loại sân, khung giờ và ngày hiệu lực cụ thể chưa
        Task<bool> ExistsAsync(Guid branchId, Guid courtTypeId, Guid timeSlotId, DateOnly effectiveFrom);

        // Tạo batch giá override mới cho 1 branch + court type với ngày hiệu lực cụ thể. Cả cặp WEEKDAY + WEEKEND phải được tạo cùng lúc.
        Task CreateBatchAsync(List<BranchPriceOverride> prices);

        // Xóa cả cặp WEEKDAY + WEEKEND; trả số bản ghi bị xóa (0 = không tìm thấy).
        Task<int> DeletePairAsync(Guid branchId, Guid courtTypeId, DateOnly effectiveFrom,
                                  TimeOnly startTime, TimeOnly endTime);

        // Lấy các giá override của 1 chi nhánh + 1 loại sân tại 1 ngày hiệu lực chính xác
        Task<List<BranchPriceOverride>> GetExactDatePricesAsync(Guid branchId, Guid courtTypeId, DateOnly effectiveFrom);

        // Upsert batch (Cập nhật nếu đã có, tạo mới nếu chưa)
        Task UpsertBatchAsync(List<BranchPriceOverride> insertPrices, List<BranchPriceOverride> updatePrices);
    }
}
