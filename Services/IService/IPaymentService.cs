using Microsoft.AspNetCore.Http;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.Payment;

namespace SmashCourt_BE.Services.IService
{
    public interface IPaymentService
    {
        // IPN của VNPay (server gọi server): nơi duy nhất ghi kết quả giao dịch VNPay
        Task HandleVnPayIpnAsync(IQueryCollection query, HttpRequest request);

        // Trình duyệt quay lại từ VNPay: chỉ kiểm chữ ký và đọc trạng thái để FE hiển thị, không ghi DB
        Task<VnPayReturnResult> HandleVnPayReturnAsync(IQueryCollection query);

        // "Thanh toán ngay" / "Thanh toán cọc": link mới đúng tiền cọc, hạn không vượt hạn giữ chỗ tuyệt đối
        Task<OnlineBookingResponse> RetryPaymentAsync(Guid bookingId, Guid customerId);
    }
}
