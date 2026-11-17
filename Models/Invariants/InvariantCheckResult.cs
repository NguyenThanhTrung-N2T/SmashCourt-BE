namespace SmashCourt_BE.Models.Invariants
{
    /// <summary>Kết quả một lượt kiểm tính đúng: số đơn trong phạm vi và các vi phạm (rỗng khi sạch).</summary>
    public sealed record InvariantCheckResult(int CheckedBookings, IReadOnlyList<InvariantViolation> Violations);
}
