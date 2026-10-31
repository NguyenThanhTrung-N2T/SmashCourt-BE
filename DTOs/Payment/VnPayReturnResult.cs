namespace SmashCourt_BE.DTOs.Payment
{
    /// <summary>
    /// Kết quả trả về từ VNPay Return URL
    /// Dùng để hiển thị thông tin thanh toán cho FE
    /// </summary>
    public class VnPayReturnResult
    {
        /// <summary>
        /// Thanh toán có thành công hay không
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Message hiển thị cho user
        /// </summary>
        public string Message { get; set; } = null!;

        /// <summary>
        /// Booking ID (nếu tìm thấy payment)
        /// </summary>
        public string? BookingId { get; set; }
        /// <summary>
        /// Mã đơn đặt sân (Booking Code)
        /// </summary>
        public string? BookingCode { get; set; }

        /// <summary>
        /// Transaction reference từ VNPay
        /// </summary>
        public string? TransactionRef { get; set; }

        /// <summary>
        /// Số tiền thanh toán (VNĐ)
        /// </summary>
        public decimal? Amount { get; set; }

        /// <summary>
        /// Response code từ VNPay (00 = success)
        /// </summary>
        public string? ResponseCode { get; set; }

        /// <summary>
        /// Trạng thái đơn đọc từ DB lúc gọi (tên enum như OnlineBookingResponse.Status); null khi chữ ký sai hoặc không tìm thấy giao dịch.
        /// FE rẽ nhánh theo trường này; IsSuccess chỉ là kết quả của cổng (N-36, P6-28)
        /// </summary>
        public string? BookingStatus { get; set; }
    }
}
