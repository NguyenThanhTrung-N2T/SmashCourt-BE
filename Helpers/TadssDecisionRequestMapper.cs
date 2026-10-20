using SmashCourt_BE.Configurations;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.ViewModels;

namespace SmashCourt_BE.Helpers
{
    /// <summary>
    /// Dựng request <c>POST /api/v2/decisions</c> từ dữ liệu booking đã đọc (spec Phase 6, 7.4 và 7.14).
    /// Hàm thuần: không đọc DB, không đọc đồng hồ; cùng đầu vào cho đúng cùng một request (TADSS so <c>request_hash</c>).
    /// </summary>
    public static class TadssDecisionRequestMapper
    {
        private const string Currency = "VND";
        private const string PromotionCodeKey = "promotionCode";

        // Thứ hạng 1…5 theo min_points tăng dần; từ hạng 6 gộp vào DIAMOND (INTEGRATION_SPEC 4.2)
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
                    IsGuest = false, // đặt online bắt buộc đăng nhập (P6-01)
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

        /// <summary>Thứ hạng (1…n) theo min_points tăng dần sang hạng của contract; chưa có hạng là STANDARD, từ hạng 6 là DIAMOND.</summary>
        public static TadssTier MapTier(int? rank)
        {
            if (rank is not { } value || value < 1)
                return TadssTier.STANDARD;
            return value > TiersByRank.Length ? TadssTier.DIAMOND : TiersByRank[value - 1];
        }

        // Giờ chơi trong DB là giờ tường VN: tính thời điểm tuyệt đối bằng ToUtcFromVietnam rồi mới biểu diễn theo offset của múi giờ chi nhánh
        private static DateTimeOffset PlayTime(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
            TimeZoneInfo.ConvertTime(Moment(DateTimeHelper.ToUtcFromVietnam(date, time)), zone);

        // Mốc thời điểm lấy từ DB phải là UTC; Kind khác Utc làm DateTimeOffset lấy offset của múi giờ máy (lỗi lệch 7 giờ kiểu T-3)
        private static DateTimeOffset Moment(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Mốc thời gian gửi TADSS phải có Kind = Utc.", nameof(utc));
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }
    }
}
