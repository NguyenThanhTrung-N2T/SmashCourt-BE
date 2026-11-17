namespace SmashCourt_BE.Models.Invariants
{
    /// <summary>Một vi phạm tính đúng dữ liệu đặt sân: điều bị vi phạm (<see cref="InvariantRules"/>), đơn trong phạm vi kiểm và mô tả để đọc.</summary>
    public sealed record InvariantViolation(string Rule, Guid BookingId, string Detail)
    {
        public override string ToString() => $"[{Rule}] {BookingId}: {Detail}";
    }
}
