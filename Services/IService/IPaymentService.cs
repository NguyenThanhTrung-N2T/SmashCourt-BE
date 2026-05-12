using Microsoft.AspNetCore.Http;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.DTOs.Payment;

namespace SmashCourt_BE.Services.IService
{
    public interface IPaymentService
    {
        /// <summary>
        /// Xử lý IPN callback từ VNPay (server-to-server)
        /// ✅ UPDATE DATABASE - đây là nơi chính thức xử lý payment
        /// </summary>
        Task HandleVnPayIpnAsync(IQueryCollection query, HttpRequest request);

        /// <summary>
        /// Xử lý Return URL từ VNPay (browser redirect)
        /// ⚠️ READ-ONLY - chỉ verify và trả về thông tin để FE hiển thị
        /// </summary>
        Task<VnPayReturnResult> HandleVnPayReturnAsync(IQueryCollection query);

        /// <summary>
        /// Tạo lại URL thanh toán VNPay cho booking PENDING đã bị gián đoạn
        /// Validate: booking thuộc customer, status = PENDING, now &lt; ExpiresAt
        /// </summary>
        Task<OnlineBookingResponse> RetryPaymentAsync(Guid bookingId, Guid customerId);
    }
}
