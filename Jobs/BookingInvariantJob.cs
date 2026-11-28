using System.Diagnostics;
using Hangfire;
using SmashCourt_BE.Infrastructure.Telemetry;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Invariants;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Jobs
{
    /// <summary>
    /// Kiểm định kỳ "0 booking hỏng" trên DB thật của bản đang chạy, bằng đúng bộ luật mà test dùng. Chỉ đọc: không sửa dữ liệu, không gửi gì cho khách.
    /// Vi phạm mới (chưa có ở lượt trước) ghi một dòng log Error kèm mã đơn; vi phạm còn tồn tại chỉ được nhắc trong dòng tổng (Warning) để log không
    /// lặp lại mỗi 10 phút. Số vi phạm theo điều và số đơn đã soát xuất qua metric để dashboard và cảnh báo email đọc.
    /// </summary>
    public class BookingInvariantJob : IBookingInvariantJob
    {
        /// <summary>Khoảng soát: đơn tạo hoặc sửa trong chừng này thời gian trước lúc chạy, cộng mọi đơn còn hiệu lực bất kể tuổi.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromHours(24);

        /// <summary>Bỏ đơn vừa đổi trong chừng này thời gian: luồng nhiều transaction (đặt online, thanh toán) có thể đang dở giữa hai bước.</summary>
        public static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(2);

        /// <summary>Số mã đơn tối đa nêu trong dòng tổng; lỗi hàng loạt thì phần còn lại chỉ đếm để dòng log không phình.</summary>
        public const int MaxListedBookings = 20;

        private readonly IBookingInvariantService _invariants;
        private readonly BookingInvariantMetrics _metrics;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<BookingInvariantJob> _logger;

        public BookingInvariantJob(
            IBookingInvariantService invariants,
            BookingInvariantMetrics metrics,
            TimeProvider timeProvider,
            ILogger<BookingInvariantJob> logger)
        {
            _invariants = invariants;
            _metrics = metrics;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        [AutomaticRetry(Attempts = 0)]
        public async Task CheckRecentBookingsAsync()
        {
            var sw = Stopwatch.StartNew();
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var scope = new BookingScope(null, now - Window, now, SettledBeforeUtc: now - SettleTime, IncludeActive: true);
            try
            {
                var result = await _invariants.CheckAsync(scope, now);
                var fresh = _metrics.RecordRun(result);

                foreach (var violation in fresh)
                    _logger.LogError("[Invariant] Đơn {BookingCode} ({BookingId}) vi phạm {Rule}: {RuleTitle}. Chi tiết: {Detail}",
                        violation.BookingLabel, violation.BookingId, violation.Rule, InvariantRules.Title(violation.Rule), violation.Detail);

                if (result.Violations.Count == 0)
                    _logger.LogInformation("[Invariant] {Checked} đơn (24 giờ và đơn còn hiệu lực), không vi phạm. Thời gian chạy: {Ms}ms",
                        result.CheckedBookings, sw.ElapsedMilliseconds);
                else
                    // Dòng tổng mỗi lượt nêu lại mã các đơn đang vi phạm: dashboard đọc dòng mới nhất là biết đơn nào còn sai, kể cả vi phạm cũ
                    _logger.Log(fresh.Count > 0 ? LogLevel.Error : LogLevel.Warning,
                        "[Invariant] {Checked} đơn (24 giờ và đơn còn hiệu lực), {Count} vi phạm, {New} mới. Đơn đang vi phạm: {Bookings}. Thời gian chạy: {Ms}ms",
                        result.CheckedBookings, result.Violations.Count, fresh.Count,
                        DescribeBookings(result.Violations), sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // Lỗi đọc DB không được làm gauge báo 0 vi phạm: giữ số của lượt trước, chỉ đếm lượt lỗi
                _metrics.RecordError();
                _logger.LogError(ex, "[Invariant] Không kiểm được tính đúng dữ liệu. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // "Đơn đã hủy không có lý do hủy (SC6-6): BK-1, BK-2; …", mỗi đơn một lần trong mỗi điều; quá giới hạn thì "… và N mục khác"
        public static string DescribeBookings(IReadOnlyList<InvariantViolation> violations)
        {
            var listed = 0;
            var parts = new List<string>();
            foreach (var group in violations.GroupBy(v => v.Rule).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var labels = group.Select(v => v.BookingLabel).Distinct().ToList();
                var shown = labels.Take(Math.Max(0, MaxListedBookings - listed)).ToList();
                listed += shown.Count;
                if (shown.Count > 0)
                    parts.Add($"{InvariantRules.Title(group.Key)} ({group.Key}): {string.Join(", ", shown)}");
            }

            var total = violations.GroupBy(v => v.Rule).Sum(g => g.Select(v => v.BookingLabel).Distinct().Count());
            return total > listed ? $"{string.Join("; ", parts)} … và {total - listed} mục khác" : string.Join("; ", parts);
        }
    }
}
