using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Repositories.IRepository
{
    public interface IBookingInvariantRepository
    {
        // Đọc dữ liệu kiểm tính đúng của các đơn trong phạm vi, mọi câu đọc trong một snapshot REPEATABLE READ (chỉ đọc, rollback khi xong)
        Task<BookingInvariantSnapshot> LoadAsync(BookingScope scope, CancellationToken ct = default);

        // Id các đơn thuộc phạm vi: danh sách cho sẵn, hoặc đơn tạo / sửa trong khoảng thời gian (trừ đơn sửa sau SettledBeforeUtc)
        Task<List<Guid>> ResolveScopeAsync(BookingScope scope, CancellationToken ct = default);
    }
}
