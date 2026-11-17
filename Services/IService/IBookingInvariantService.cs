using SmashCourt_BE.Models.Invariants;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingInvariantService
    {
        // Kiểm tính đúng dữ liệu của các đơn trong phạm vi (chỉ đọc, một snapshot); nowUtc là "bây giờ" của lượt kiểm, phải là UTC
        Task<InvariantCheckResult> CheckAsync(BookingScope scope, DateTime nowUtc, CancellationToken ct = default);
    }
}
