namespace SmashCourt_BE.Models.Enums
{
    /// <summary>
    /// Thời điểm thanh toán cho booking
    /// </summary>
    public enum PaymentTiming
    {
        /// <summary>
        /// Thanh toán trước - khách thanh toán khi đặt sân
        /// </summary>
        PREPAID = 0,

        /// <summary>
        /// Thanh toán sau - khách thanh toán sau khi chơi xong
        /// </summary>
        POSTPAID = 1
    }
}
