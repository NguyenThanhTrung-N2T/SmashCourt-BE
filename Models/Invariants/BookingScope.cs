namespace SmashCourt_BE.Models.Invariants
{
    /// <summary>
    /// Phạm vi kiểm: theo danh sách id (test) hoặc theo khoảng thời gian [FromUtc; ToUtc] (staging, kiểm định kỳ). Khoảng thời gian lấy đơn có
    /// <c>created_at</c> <b>hoặc</b> <c>updated_at</c> trong khoảng, để gồm cả đơn có sẵn vừa bị sửa. <see cref="SettledBeforeUtc"/> bỏ đơn vừa đổi
    /// (<c>updated_at</c> sau mốc đó): luồng nhiều transaction (đặt online, thanh toán) có lúc đơn đang dở giữa hai bước.
    /// <see cref="IncludeActive"/> cộng thêm mọi đơn còn hiệu lực bất kể lúc tạo / sửa, để vi phạm của đơn chưa kết thúc không rời phạm vi khi chưa ai sửa.
    /// </summary>
    public sealed record BookingScope(
        IReadOnlyCollection<Guid>? BookingIds, DateTime? FromUtc, DateTime? ToUtc, DateTime? SettledBeforeUtc = null,
        bool IncludeActive = false)
    {
        public static BookingScope Of(params Guid[] bookingIds) => new(bookingIds, null, null);

        public static BookingScope Of(IReadOnlyCollection<Guid> bookingIds) => new(bookingIds, null, null);

        public static BookingScope Between(DateTime fromUtc, DateTime toUtc) => new(null, fromUtc, toUtc);
    }
}
