using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Repositories;
using SmashCourt_BE.Services;

namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>
/// Cửa vào của test tới bộ kiểm SC6 "0 booking hỏng" (BE-IT-20) dùng chung với job kiểm định kỳ của BE: luật ở <c>Services/Invariants/BookingInvariantRules</c>,
/// câu đọc ở <see cref="BookingInvariantRepository"/>. Test truyền thẳng DbContext của mình (kể cả DbContext đang có transaction rollback).
/// </summary>
public static class BookingInvariantChecker
{
    public static BookingStatus[] CourtsReleasedWhileActive => InvariantRules.CourtsReleasedWhileActive;

    public static async Task<IReadOnlyList<InvariantViolation>> CheckAsync(
        SmashCourtContext db, BookingScope scope, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var result = await new BookingInvariantService(new BookingInvariantRepository(db)).CheckAsync(scope, nowUtc, ct);
        return result.Violations;
    }

    internal static Task<T> InSnapshotAsync<T>(SmashCourtContext db, Func<Task<T>> read, CancellationToken ct = default) =>
        BookingInvariantRepository.InSnapshotAsync(db, read, ct);

    // Dùng lại ở lượt kiểm staging để lấy đúng tập đơn trong cùng snapshot
    internal static Task<List<Guid>> ResolveScopeAsync(SmashCourtContext db, BookingScope scope, CancellationToken ct) =>
        new BookingInvariantRepository(db).ResolveScopeAsync(scope, ct);
}
