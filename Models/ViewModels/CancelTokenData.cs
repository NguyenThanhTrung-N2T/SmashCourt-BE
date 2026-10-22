namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — token hủy bằng link vừa sinh (spec Phase 6, 7.9, D8). Chỉ <see cref="TokenHash"/> và
    /// <see cref="ExpiresAtUtc"/> được lưu DB; <see cref="RawToken"/> chỉ sống trong request để đưa vào email.
    /// </summary>
    /// <param name="RawToken">Token gốc, URL-safe, đặt vào link hủy trong email</param>
    /// <param name="TokenHash">SHA-256 hex chữ thường của token gốc (<c>bookings.cancel_token_hash</c>)</param>
    /// <param name="ExpiresAtUtc">Hạn link hủy, Kind = Utc (<c>bookings.cancel_token_expires_at</c>)</param>
    public sealed record CancelTokenData(string RawToken, string TokenHash, DateTime ExpiresAtUtc);
}
