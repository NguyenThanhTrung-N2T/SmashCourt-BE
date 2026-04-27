using SmashCourt_BE.Models.Entities;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface ISystemPriceRepository
    {
        // Lịch sử toàn bộ giá chung — filter theo court type nếu có
        Task<List<SystemPrice>> GetAllAsync(Guid? courtTypeId = null);

        // Lấy giá chung hiện tại — filter theo court type nếu có
        Task<List<SystemPrice>> GetCurrentAsync(Guid? courtTypeId = null);

        // Lấy giá chung tại một thời điểm cụ thể — filter theo court type nếu có
        Task<List<SystemPrice>> GetCurrentForDateAsync(DateOnly targetDate, Guid? courtTypeId = null);

        // Kiểm tra xem đã tồn tại giá chung cho court type, time slot và effective from chưa
        Task<bool> ExistsAsync(Guid courtTypeId, Guid timeSlotId, DateOnly effectiveFrom);

        // Tạo mới một batch giá chung (áp dụng từ ngày nào, cho court type nào, giá như thế nào)
        Task CreateBatchAsync(List<SystemPrice> prices);

        // Lấy giá chung hiện tại cho một court type cụ thể (dùng trong booking để tính giá)
        Task<List<SystemPrice>> GetCurrentRawAsync(Guid courtTypeId);
        // Lấy danh sách các ngày hiệu lực của giá chung cho một loại sân cụ thể
        Task<List<DateOnly>> GetVersionsAsync(Guid courtTypeId);

        // Lấy các giá chung của 1 loại sân tại 1 ngày hiệu lực chính xác
        Task<List<SystemPrice>> GetExactDatePricesAsync(Guid courtTypeId, DateOnly effectiveFrom);

        // Upsert batch (Cập nhật nếu đã có, tạo mới nếu chưa)
        Task UpsertBatchAsync(List<SystemPrice> insertPrices, List<SystemPrice> updatePrices);
    }
}
