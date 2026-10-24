namespace SmashCourt_BE.Models.Enums
{
    /// <summary>
    /// Nguồn của mức cọc trên hóa đơn
    /// </summary>
    public enum DepositSource
    {
        /// <summary>TADSS quyết định mức cọc</summary>
        TADSS = 0,

        /// <summary>Manager duyệt mức cọc</summary>
        MANAGER = 1,

        /// <summary>Không có quyết định từ TADSS (tắt cờ, lỗi, không kịp duyệt) nên thu 100%</summary>
        FULL_PAYMENT_FALLBACK = 2
    }
}
