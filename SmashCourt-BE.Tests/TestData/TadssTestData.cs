using SmashCourt_BE.Integrations.Tadss.DTOs;

namespace SmashCourt_BE.Tests.TestData;

/// <summary>Dữ liệu mẫu dùng chung cho các test của TADSS (request đánh giá, override, outcome).</summary>
internal static class TadssTestData
{
    public static readonly Guid BookingId = Guid.Parse("5b0a6f0e-7c0d-4a0e-9a64-0c2e7f1b9a11");
    public static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d003");

    /// <summary><paramref name="full"/> = có đủ trường tùy chọn (lịch sử, nhu cầu, metadata); sai thì chỉ trường bắt buộc.</summary>
    public static TadssDecisionRequestDto DecisionRequest(bool full) => new()
    {
        ReservationId = BookingId,
        BranchId = Guid.Parse("1f2e3d4c-0000-4000-8000-000000000001"),
        BranchTimeZone = "Asia/Ho_Chi_Minh",
        Items =
        [
            new TadssDecisionItemDto
            {
                ResourceId = Guid.Parse("c0a8012e-0000-4000-8000-0000000000aa"),
                ResourceTypeId = Guid.Parse("c0a8012e-0000-4000-8000-0000000000bb"),
                StartTime = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(7)),
                EndTime = new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.FromHours(7)),
                Price = 240000m
            }
        ],
        Customer = new TadssCustomerDto
        {
            CustomerId = Guid.Parse("9d3f1c2a-0000-4000-8000-000000000123"),
            IsGuest = false,
            Tier = TadssTier.GOLD,
            MemberSince = full ? new DateTimeOffset(2025, 3, 14, 1, 0, 0, TimeSpan.Zero) : null,
            History = full
                ? new TadssHistoryDto
                {
                    CompletedCount = 12, NoShowCount = 1, CancelledCount = 2, LateCancelledCount = 1,
                    PaymentExpiredCount = 0, LastCompletedAt = new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.FromHours(7))
                }
                : null
        },
        TotalPrice = 228000m,
        Currency = "VND",
        Channel = TadssChannel.ONLINE,
        Demand = full ? new TadssDemandDto { OccupancyRate = 0.75m, WaitlistCount = 2 } : null,
        RequestedAt = new DateTimeOffset(2026, 11, 18, 9, 15, 0, TimeSpan.FromHours(7)),
        Metadata = full ? new Dictionary<string, string> { ["promotionCode"] = "WINTER" } : null
    };

    public static TadssOverrideRequestDto OverrideApprove() => new()
    {
        Action = TadssOverrideAction.APPROVE,
        Deposit = new TadssOverrideDepositDto { Rate = 0.3m },
        Reason = "Khách quen, chi nhánh bảo lãnh"
    };

    public static TadssOverrideRequestDto OverrideReject() => new()
    {
        Action = TadssOverrideAction.REJECT,
        Reason = "Sân bảo trì vào giờ này"
    };

    public static TadssOutcomeRequestDto OutcomeNoShow() => new()
    {
        Outcome = TadssOutcome.NO_SHOW,
        OccurredAt = new DateTimeOffset(2026, 11, 20, 18, 30, 0, TimeSpan.FromHours(7))
    };
}
