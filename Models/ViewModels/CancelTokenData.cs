namespace SmashCourt_BE.Models.ViewModels
{
    /// <summary>
    /// Internal model — token hủy bằng link vừa sinh. Chỉ <see cref="TokenHash"/> và <see cref="ExpiresAtUtc"/> được lưu DB;
    /// <see cref="RawToken"/> chỉ sống trong request để đưa vào email.
    /// </summary>
    public sealed record CancelTokenData(string RawToken, string TokenHash, DateTime ExpiresAtUtc);
}
