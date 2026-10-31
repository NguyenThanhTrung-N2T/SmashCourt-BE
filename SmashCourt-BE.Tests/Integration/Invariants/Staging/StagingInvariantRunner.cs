using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmashCourt_BE.Data;
using SmashCourt_BE.Extensions;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>
/// Một lượt kiểm SC6 trên staging (BE-IT-20, P6-29; plan 6A-6 Task 11). Tập dữ liệu cố định (R28): khoảng thời gian bắt buộc và đã kết thúc
/// trước lúc chạy; danh sách đơn và <c>decisionId</c> lấy một lần trong snapshot cùng lúc với điều 1–4, 6, rồi mới gọi TADSS cho điều 5 trên
/// đúng danh sách đó. Báo cáo ghi một lần sau khi mọi bước xong; lỗi giữa chừng ⇒ <c>INCONCLUSIVE</c> kèm lý do, không bao giờ <c>PASS</c>.
/// </summary>
public static class StagingInvariantRunner
{
    private sealed record ExecutionRow(Guid DecisionId, Guid ReservationId);

    // Một data source cho mỗi chuỗi kết nối, dùng lại giữa các lượt: EF Core dựng một service provider nội bộ cho mỗi data source khác nhau
    // và ném ManyServiceProvidersCreatedWarning khi quá 20 trong một tiến trình (bộ nhớ đệm dùng chung cả tiến trình test)
    private static readonly ConcurrentDictionary<string, NpgsqlDataSource> DataSources = new();

    private sealed record Snapshot(
        IReadOnlyList<InvariantViolation> Violations, int CheckedBookings, List<(Guid BookingId, Guid DecisionId)> Decisions,
        List<OrphanDecision> Orphans, string? OrphanError);

    /// <summary>Data source dùng chung của chuỗi kết nối (test seed dữ liệu bằng chính nó để không sinh thêm service provider của EF).</summary>
    internal static NpgsqlDataSource DataSourceFor(string connectionString) =>
        DataSources.GetOrAdd(connectionString, cs => new NpgsqlDataSourceBuilder(cs).MapSmashCourtEnums().Build());

    public static async Task<StagingInvariantReport> RunAsync(StagingRunOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clock = options.Clock ?? (() => DateTime.UtcNow);
        var startedAt = clock();
        ValidateWindow(options, startedAt); // cấu hình sai: không phải một lượt kiểm, không ghi file
        var window = new ReportWindow(options.FromUtc, options.ToUtc);

        StagingInvariantReport report;
        try
        {
            report = await RunCoreAsync(options, window, startedAt, clock, ct);
        }
        catch (Exception ex)
        {
            report = StagingInvariantReport.Aborted(window, startedAt, clock(), $"{ex.GetType().Name}: {ex.Message}");
        }

        if (options.OutputPath != null)
            await report.WriteAtomicallyAsync(options.OutputPath);
        return report;
    }

    private static void ValidateWindow(StagingRunOptions options, DateTime startedAt)
    {
        if (options.FromUtc.Kind != DateTimeKind.Utc || options.ToUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("SC6_FROM_UTC, SC6_TO_UTC phải là UTC.");
        if (options.FromUtc > options.ToUtc)
            throw new ArgumentException($"SC6_FROM_UTC ({options.FromUtc:O}) sau SC6_TO_UTC ({options.ToUtc:O}).");
        if (options.ToUtc > startedAt)
            throw new ArgumentException(
                $"SC6_TO_UTC ({options.ToUtc:O}) sau lúc bắt đầu kiểm ({startedAt:O}): khoảng chưa kết thúc nên tập dữ liệu không cố định.");
    }

    private static async Task<StagingInvariantReport> RunCoreAsync(
        StagingRunOptions options, ReportWindow window, DateTime startedAt, Func<DateTime> clock, CancellationToken ct)
    {
        var dataSource = DataSourceFor(options.ConnectionString);
        await using var db = new SmashCourtContext(new DbContextOptionsBuilder<SmashCourtContext>().UseNpgsql(dataSource).Options);

        var snapshot = await BookingInvariantChecker.InSnapshotAsync(db, () => ReadSnapshotAsync(db, options, startedAt, ct), ct);

        var violations = snapshot.Violations.ToList();
        var inconclusive = new List<InconclusiveCheck>();
        foreach (var check in await CheckDecisionsAsync(options, snapshot.Decisions, ct))
        {
            if (check.Outcome == DecisionCheckOutcome.Missing)
                violations.Add(new InvariantViolation(InvariantRules.DecisionExistsInTadss, check.BookingId,
                    $"tadss_decision_id {check.DecisionId} không có ở TADSS ({check.Reason})"));
            else if (check.Outcome == DecisionCheckOutcome.Inconclusive)
                inconclusive.Add(new InconclusiveCheck(InvariantRules.DecisionExistsInTadss, check.BookingId, check.DecisionId,
                    check.Reason ?? "không rõ"));
        }

        return new StagingInvariantReport(window, startedAt, clock(), snapshot.CheckedBookings, snapshot.Decisions.Count,
            violations.OrderBy(v => v.Rule, StringComparer.Ordinal).ThenBy(v => v.BookingId).ToList(), inconclusive, snapshot.Orphans,
            snapshot.OrphanError);
    }

    // Trong một snapshot: điều 1–4, 6 (now = lúc bắt đầu kiểm), tập đơn và decisionId để kiểm điều 5, và chẩn đoán quyết định mồ côi (cuối cùng:
    // lỗi đọc bảng của TADSS làm hỏng transaction nên không được chạy trước các câu đọc khác)
    private static async Task<Snapshot> ReadSnapshotAsync(SmashCourtContext db, StagingRunOptions options, DateTime now, CancellationToken ct)
    {
        var scope = BookingScope.Between(options.FromUtc, options.ToUtc);
        var violations = await BookingInvariantChecker.CheckAsync(db, scope, now, ct);
        var ids = await BookingInvariantChecker.ResolveScopeAsync(db, scope, ct);
        var decisions = (await db.Bookings.AsNoTracking()
                .Where(b => ids.Contains(b.Id) && b.TadssDecisionId != null)
                .Select(b => new { b.Id, b.TadssDecisionId })
                .ToListAsync(ct))
            .Select(b => (b.Id, b.TadssDecisionId.GetValueOrDefault())) // đã lọc != null ở câu SQL
            .ToList();

        try
        {
            var orphans = await FindOrphansAsync(db, options.FromUtc, options.ToUtc, ct);
            return new Snapshot(violations, ids.Count, decisions, orphans, null);
        }
        catch (PostgresException ex)
        {
            return new Snapshot(violations, ids.Count, decisions, [], $"Không đọc được tadss.decision_executions: {ex.MessageText}");
        }
    }

    // Chẩn đoán (không phải điều kiện của SC6): quyết định PRIMARY của TADSS trong khoảng mà đơn BE cùng id không lưu đúng decision đó.
    // Phụ thuộc có chủ đích vào schema nội bộ của TADSS, chỉ trong công cụ staging (TADSS không có API liệt kê theo reservationId)
    private static async Task<List<OrphanDecision>> FindOrphansAsync(SmashCourtContext db, DateTime from, DateTime to, CancellationToken ct)
    {
        var executions = await db.Database.SqlQuery<ExecutionRow>(
                $"""
                 SELECT decision_id AS "DecisionId", reservation_id AS "ReservationId"
                 FROM tadss.decision_executions
                 WHERE role = 'PRIMARY' AND created_at >= {from} AND created_at <= {to}
                 """)
            .ToListAsync(ct);
        if (executions.Count == 0)
            return [];

        var reservationIds = executions.Select(e => e.ReservationId).Distinct().ToList();
        var stored = await db.Bookings.AsNoTracking()
            .Where(b => reservationIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.TadssDecisionId, ct);

        return executions
            .Where(e => !stored.TryGetValue(e.ReservationId, out var decisionId) || decisionId != e.DecisionId)
            .Select(e => new OrphanDecision(e.DecisionId, e.ReservationId,
                !stored.TryGetValue(e.ReservationId, out var decisionId) ? "BE không có đơn này"
                : decisionId == null ? "đơn BE không lưu tadss_decision_id"
                : $"đơn BE lưu quyết định khác {decisionId}"))
            .OrderBy(o => o.ReservationId).ThenBy(o => o.DecisionId)
            .ToList();
    }

    private static async Task<IReadOnlyList<DecisionCheck>> CheckDecisionsAsync(
        StagingRunOptions options, List<(Guid BookingId, Guid DecisionId)> decisions, CancellationToken ct)
    {
        if (decisions.Count == 0)
            return [];
        if (options.TadssBaseUrl == null)
            return decisions.Select(d => new DecisionCheck(d.BookingId, d.DecisionId, DecisionCheckOutcome.Inconclusive, "thiếu TADSS_BASE_URL"))
                .ToList();

        var timeout = options.DecisionTimeout ?? TadssDecisionProbe.DefaultTimeout;
        using var client = new HttpClient(options.TadssHandler ?? new HttpClientHandler(), disposeHandler: options.TadssHandler == null)
        {
            BaseAddress = options.TadssBaseUrl,
            Timeout = timeout + TimeSpan.FromSeconds(5) // timeout từng request do probe quản; client chỉ là lưới an toàn
        };
        if (options.TadssApiKey != null)
            client.DefaultRequestHeaders.Add("X-Api-Key", options.TadssApiKey);
        return await TadssDecisionProbe.CheckAsync(client, decisions, timeout: timeout, ct: ct);
    }
}
