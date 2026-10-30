namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>Một vi phạm SC6: điều bị vi phạm (<see cref="InvariantRules"/>), đơn trong phạm vi kiểm và mô tả để đọc.</summary>
public sealed record InvariantViolation(string Rule, Guid BookingId, string Detail)
{
    public override string ToString() => $"[{Rule}] {BookingId}: {Detail}";
}

/// <summary>Tên các điều của SC6 (plan 6A-6 Task 9, spec P6-29). Giá trị dùng trong báo cáo JSON của lượt kiểm staging.</summary>
public static class InvariantRules
{
    public const string ConfirmedIsPaid = "SC6-1";
    public const string AwaitingApprovalHoldsLocks = "SC6-2";
    public const string CancelledHoldsNothing = "SC6-3";
    public const string ActiveCourtsAreActive = "SC6-4a";
    public const string NoDoubleBooking = "SC6-4b";
    public const string CancelledHasReason = "SC6-6"; // nâng từ điều phụ 31/10: lý do hủy là đầu vào của TADSS (lịch sử khách, outcome)
    public const string PendingHasHoldDeadline = "PHU-2";
}
