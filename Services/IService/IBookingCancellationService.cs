using SmashCourt_BE.DTOs.Booking;

namespace SmashCourt_BE.Services.IService
{
    public interface IBookingCancellationService
    {
        // Nhân viên hủy đơn chi nhánh mình (chưa trả, chờ duyệt, đã xác nhận, đã thanh toán online); hoàn theo chính sách
        Task CancelByStaffAsync(Guid bookingId, Guid staffId, string role);

        // Khách hủy đơn của mình trong app; đã tới giờ bắt đầu ⇒ 400 BOOKING_ALREADY_STARTED
        Task CancelByCustomerAsync(Guid bookingId, Guid customerId);

        // Khách hủy bằng link trong email; token chỉ bị tiêu khi lệnh hủy thành công
        Task CancelByTokenAsync(string token);

        // Xem trước khi hủy bằng link: số hoàn tính cùng công thức với lệnh hủy
        Task<CancelTokenInfoDto> GetCancelInfoAsync(string token);
    }
}
