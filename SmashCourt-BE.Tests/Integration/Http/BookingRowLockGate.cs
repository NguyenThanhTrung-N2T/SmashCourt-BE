using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Giữ khóa dòng của một booking trong transaction riêng để dựng thứ tự đua một cách tất định: mọi luồng ghi đơn (thêm/xóa dịch vụ, check-out,
/// hủy, IPN) đều khóa dòng này trước, nên request gửi vào sẽ nạp đơn xong rồi đứng chờ ở đúng chỗ lấy khóa. Đợi đủ số phiên đang chờ
/// (theo <c>pg_blocking_pids</c>) rồi mới nhả; phiên xếp hàng trước lấy khóa trước.
/// </summary>
internal sealed class BookingRowLockGate : IAsyncDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private readonly IServiceProvider _services;
    private readonly IServiceScope _scope;
    private readonly IDbContextTransaction _transaction;
    private readonly int _pid;

    private BookingRowLockGate(IServiceProvider services, IServiceScope scope, IDbContextTransaction transaction, int pid)
    {
        _services = services;
        _scope = scope;
        _transaction = transaction;
        _pid = pid;
    }

    public static async Task<BookingRowLockGate> HoldAsync(IServiceProvider services, Guid bookingId)
    {
        var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmashCourtContext>();
        var transaction = await db.Database.BeginTransactionAsync();
        // Ghi lại đúng giá trị cũ: chỉ để lấy khóa dòng, dữ liệu không đổi
        var locked = await db.Bookings.Where(b => b.Id == bookingId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.UpdatedAt, b => b.UpdatedAt));
        if (locked != 1)
            throw new InvalidOperationException($"Không khóa được booking {bookingId}.");
        var pid = await db.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        return new BookingRowLockGate(services, scope, transaction, pid);
    }

    /// <summary>Đợi tới khi đúng <paramref name="count"/> phiên đang chờ khóa này; request nào đã xong sớm (chưa tới chỗ khóa) thì báo lỗi ngay.</summary>
    public async Task WaitForBlockedAsync(int count, params Task[] started)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        var blocked = 0;
        while (DateTime.UtcNow < deadline)
        {
            if (started.FirstOrDefault(t => t.IsCompleted) is { } finished)
                throw new InvalidOperationException(
                    $"Một request đã xong trước khi chờ khóa booking (trạng thái task {finished.Status}); thứ tự đua không dựng được.");

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SmashCourtContext>();
            // Phiên chờ thứ hai trở đi chờ khóa tuple của phiên xếp trước (không trực tiếp chờ cổng) nên đếm cả chuỗi chờ bắt đầu từ cổng
            blocked = await db.Database.SqlQuery<int>($"""
                WITH RECURSIVE waiting(pid) AS (
                    SELECT pid FROM pg_stat_activity WHERE {_pid} = ANY(pg_blocking_pids(pid))
                    UNION
                    SELECT s.pid FROM pg_stat_activity s JOIN waiting w ON w.pid = ANY(pg_blocking_pids(s.pid)))
                SELECT count(*)::int AS "Value" FROM waiting
                """).SingleAsync();
            if (blocked == count)
                return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Sau {WaitLimit.TotalSeconds:0} giây có {blocked} phiên chờ khóa booking, kỳ vọng {count}.");
    }

    public Task ReleaseAsync() => _transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        _scope.Dispose();
    }
}
