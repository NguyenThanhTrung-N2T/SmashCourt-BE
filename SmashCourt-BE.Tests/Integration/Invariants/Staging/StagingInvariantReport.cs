using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

public sealed record ReportWindow(DateTime FromUtc, DateTime ToUtc);

public sealed record InconclusiveCheck(string Rule, Guid BookingId, Guid? DecisionId, string Reason);

public sealed record OrphanDecision(Guid DecisionId, Guid ReservationId, string Reason);

/// <summary>
/// Báo cáo một lượt kiểm SC6 trên staging (plan 6A-6 Task 11, R28). <see cref="Status"/>: <c>PASS</c> chỉ khi 0 vi phạm <b>và</b> 0 mục không kết
/// luận được; <c>FAIL</c> khi có vi phạm; <c>INCONCLUSIVE</c> khi không vi phạm nhưng còn mục không kết luận được, hoặc lượt dừng vì lỗi
/// (<see cref="AbortReason"/>). Quyết định mồ côi chỉ là chẩn đoán, không tính vào trạng thái.
/// </summary>
public sealed record StagingInvariantReport(
    ReportWindow Window,
    DateTime StartedAt,
    DateTime FinishedAt,
    int CheckedBookings,
    int CheckedDecisions,
    IReadOnlyList<InvariantViolation> Violations,
    IReadOnlyList<InconclusiveCheck> InconclusiveChecks,
    IReadOnlyList<OrphanDecision> OrphanDecisions,
    string? OrphanCheckError = null,
    string? AbortReason = null)
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string Inconclusive = "INCONCLUSIVE";

    public string Status => Violations.Count > 0 ? Fail
        : InconclusiveChecks.Count > 0 || AbortReason != null ? Inconclusive
        : Pass;

    public IReadOnlyDictionary<string, int> ViolationsByRule =>
        Violations.GroupBy(v => v.Rule).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Lượt dừng giữa chừng: không bao giờ là PASS.</summary>
    public static StagingInvariantReport Aborted(ReportWindow window, DateTime startedAt, DateTime finishedAt, string reason) =>
        new(window, startedAt, finishedAt, 0, 0, [], [], [], AbortReason: reason);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // giữ tiếng Việt dễ đọc trong file báo cáo
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(new
    {
        window = Window,
        startedAt = StartedAt,
        finishedAt = FinishedAt,
        checkedBookings = CheckedBookings,
        checkedDecisions = CheckedDecisions,
        status = Status,
        violationsByRule = ViolationsByRule,
        violations = Violations,
        inconclusiveChecks = InconclusiveChecks,
        orphanDecisions = OrphanDecisions,
        orphanCheckError = OrphanCheckError,
        abortReason = AbortReason
    }, JsonOptions);

    /// <summary>Ghi một lần: file tạm cùng thư mục rồi đổi tên (đè file cũ), nên không bao giờ có file đọc dở.</summary>
    public async Task WriteAtomicallyAsync(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, ToJson());
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
