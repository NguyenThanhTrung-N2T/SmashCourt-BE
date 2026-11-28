namespace SmashCourt_BE.Models.Invariants
{
    /// <summary>
    /// Một vi phạm tính đúng dữ liệu đặt sân: điều bị vi phạm (<see cref="InvariantRules"/>), đơn trong phạm vi kiểm và mô tả để đọc.
    /// <see cref="BookingCode"/> là mã đơn nhân viên tra được (BK-…); null khi nơi tạo không có mã.
    /// </summary>
    public sealed record InvariantViolation(string Rule, Guid BookingId, string Detail, string? BookingCode = null)
    {
        public string BookingLabel => BookingCode ?? BookingId.ToString();

        public override string ToString() => BookingCode == null
            ? $"[{Rule}] {BookingId}: {Detail}"
            : $"[{Rule}] {BookingCode} ({BookingId}): {Detail}";
    }
}
