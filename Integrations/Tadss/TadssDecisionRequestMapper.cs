using SmashCourt_BE.Configurations;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Deposit;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Dựng request đánh giá cọc gửi TADSS từ dữ liệu booking đã đọc. Hàm thuần: cùng đầu vào cho cùng request,
/// vì TADSS so khớp request khi gọi lại cùng khóa chống trùng.
/// </summary>
public static class TadssDecisionRequestMapper
{
    private const string Currency = "VND";
    private const string PromotionCodeKey = "promotionCode";

    // Thứ hạng 1…5 theo min_points tăng dần; từ hạng 6 gộp vào DIAMOND
    private static readonly TadssTier[] TiersByRank =
        [TadssTier.STANDARD, TadssTier.SILVER, TadssTier.GOLD, TadssTier.PLATINUM, TadssTier.DIAMOND];

    public static TadssDecisionRequestDto Map(
        BookingDecisionSnapshot snapshot, CustomerHistorySummary history, TadssSettings settings)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.BranchTimeZone);

        return new TadssDecisionRequestDto
        {
            ReservationId = snapshot.BookingId,
            BranchId = snapshot.BranchId,
            BranchTimeZone = settings.BranchTimeZone,
            Items = snapshot.Courts.Select(court => new TadssDecisionItemDto
            {
                ResourceId = court.CourtId,
                ResourceTypeId = court.CourtTypeId,
                StartTime = PlayTime(court.Date, court.StartTime, zone),
                EndTime = PlayTime(court.Date, court.EndTime, zone),
                Price = court.Price
            }).ToList(),
            Customer = new TadssCustomerDto
            {
                CustomerId = snapshot.CustomerId,
                IsGuest = false, // đặt online bắt buộc đăng nhập
                Tier = MapTier(snapshot.TierRank),
                MemberSince = Moment(snapshot.CustomerCreatedAt),
                History = new TadssHistoryDto
                {
                    CompletedCount = history.CompletedCount,
                    NoShowCount = history.NoShowCount,
                    CancelledCount = history.CancelledCount,
                    LateCancelledCount = history.LateCancelledCount,
                    PaymentExpiredCount = history.PaymentExpiredCount,
                    LastCompletedAt = history.LastCompletedAtUtc is { } lastCompleted
                        ? TimeZoneInfo.ConvertTime(Moment(lastCompleted), zone)
                        : null
                }
            },
            TotalPrice = snapshot.FinalTotal,
            Currency = Currency,
            Channel = TadssChannel.ONLINE,
            RequestedAt = Moment(snapshot.CreatedAt),
            Metadata = string.IsNullOrWhiteSpace(snapshot.PromotionCode)
                ? null
                : new Dictionary<string, string> { [PromotionCodeKey] = snapshot.PromotionCode }
        };
    }

    // Thứ hạng theo min_points tăng dần sang hạng của TADSS; chưa có hạng là STANDARD
    public static TadssTier MapTier(int? rank)
    {
        if (rank is not { } value || value < 1)
            return TadssTier.STANDARD;
        return value > TiersByRank.Length ? TadssTier.DIAMOND : TiersByRank[value - 1];
    }

    // Giờ chơi trong DB là giờ tường VN: đổi ra thời điểm tuyệt đối trước, rồi biểu diễn theo offset của múi giờ chi nhánh
    private static DateTimeOffset PlayTime(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(Moment(DateTimeHelper.ToUtcFromVietnam(date, time)), zone);

    // Kind khác Utc làm DateTimeOffset lấy offset của múi giờ máy, gửi sai thời điểm
    private static DateTimeOffset Moment(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Mốc thời gian gửi TADSS phải có Kind = Utc.", nameof(utc));
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
