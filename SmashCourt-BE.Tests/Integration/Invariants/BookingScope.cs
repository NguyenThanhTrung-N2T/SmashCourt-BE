namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// Phạm vi kiểm: theo danh sách id (BE-IT) hoặc theo khoảng thời gian [FromUtc; ToUtc] (staging). Khoảng thời gian lấy đơn có
/// <c>created_at</c> <b>hoặc</b> <c>updated_at</c> trong khoảng, để gồm cả đơn có sẵn bị kịch bản sửa.
/// </summary>
public sealed record BookingScope(IReadOnlyCollection<Guid>? BookingIds, DateTime? FromUtc, DateTime? ToUtc)
{
    public static BookingScope Of(params Guid[] bookingIds) => new(bookingIds, null, null);

    public static BookingScope Of(IReadOnlyCollection<Guid> bookingIds) => new(bookingIds, null, null);

    public static BookingScope Between(DateTime fromUtc, DateTime toUtc) => new(null, fromUtc, toUtc);
}
