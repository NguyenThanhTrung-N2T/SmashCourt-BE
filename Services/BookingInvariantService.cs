using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Services.Invariants;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Services;

/// <summary>Kiểm tính đúng dữ liệu đặt sân: đọc một snapshot qua repository rồi áp <see cref="BookingInvariantRules"/>.</summary>
public class BookingInvariantService : IBookingInvariantService
{
    private readonly IBookingInvariantRepository _repository;

    public BookingInvariantService(IBookingInvariantRepository repository)
    {
        _repository = repository;
    }

    public async Task<InvariantCheckResult> CheckAsync(BookingScope scope, DateTime nowUtc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc phải có Kind = Utc.", nameof(nowUtc));
        ValidateScope(scope);

        var snapshot = await _repository.LoadAsync(scope, ct);
        return new InvariantCheckResult(snapshot.ScopedBookingIds.Count, BookingInvariantRules.Evaluate(snapshot, nowUtc));
    }

    private static void ValidateScope(BookingScope scope)
    {
        if (scope.SettledBeforeUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("SettledBeforeUtc phải có Kind = Utc.", nameof(scope));
        if (scope.BookingIds != null)
            return;
        if (scope.FromUtc is not { } from || scope.ToUtc is not { } to)
            throw new ArgumentException("Phạm vi phải có danh sách id hoặc đủ FromUtc và ToUtc.", nameof(scope));
        if (from.Kind != DateTimeKind.Utc || to.Kind != DateTimeKind.Utc)
            throw new ArgumentException("FromUtc, ToUtc phải có Kind = Utc.", nameof(scope));
        if (from > to)
            throw new ArgumentException("FromUtc phải trước hoặc bằng ToUtc.", nameof(scope));
    }
}
