using System.Diagnostics;
using Hangfire;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Jobs.Interfaces;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Repositories.IRepository;

namespace SmashCourt_BE.Jobs
{
    /// <summary>
    /// Báo kết quả thực tế của đơn về TADSS (spec 7.11, P6-19, P6-26): nhãn để đánh giá chất lượng quyết định cọc, không hiển thị ở UI.
    /// Mỗi chu kỳ (5 phút) lấy tối đa 20 đơn đã kết thúc chưa báo, mới cập nhật trước, gửi tuần tự để không tranh rate limit với lượt đặt sân.
    /// 200 hoặc 409 OUTCOME_ALREADY_RECORDED ⇒ đánh dấu đã báo; TADSS từ chối riêng một đơn ⇒ để chu kỳ sau, làm tiếp; mọi lỗi hệ thống
    /// (timeout, mạng, 5xx, 429, 401, phản hồi không dùng được) ⇒ dừng chu kỳ, chu kỳ sau thử lại (dạng tối giản của circuit breaker).
    /// Dòng booking là outbox: có tadss_decision_id và tadss_outcome_reported_at trống là chưa gửi. Không đọc cờ EnableTadssDecision:
    /// đơn có quyết định từ trước khi tắt cờ vẫn được báo.
    /// </summary>
    public class TadssOutcomeJob : ITadssOutcomeJob
    {
        /// <summary>Số đơn tối đa mỗi chu kỳ (P6-19).</summary>
        private const int BatchSize = 20;

        private readonly IBookingRepository _bookingRepo;
        private readonly ITadssClient _tadssClient;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<TadssOutcomeJob> _logger;

        public TadssOutcomeJob(
            IBookingRepository bookingRepo,
            ITadssClient tadssClient,
            TimeProvider timeProvider,
            ILogger<TadssOutcomeJob> logger)
        {
            _bookingRepo = bookingRepo;
            _tadssClient = tadssClient;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        [DisableConcurrentExecution(timeoutInSeconds: 10)]
        public async Task ReportOutcomesAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var vietnamNow = DateTimeHelper.ToVietnamTime(now);
                var candidates = await _bookingRepo.GetOutcomeCandidatesAsync(
                    DateOnly.FromDateTime(vietnamNow), TimeOnly.FromDateTime(vietnamNow), BatchSize);
                if (candidates.Count == 0)
                {
                    _logger.LogInformation("[Outcome] Không có đơn nào cần báo. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
                    return;
                }

                var cycle = new Cycle();
                foreach (var booking in candidates)
                {
                    if (!await ReportAsync(booking, now, cycle))
                        break;
                }

                LogSummary(candidates, cycle, now, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Outcome] Gặp lỗi không mong muốn. Thời gian chạy: {Ms}ms", sw.ElapsedMilliseconds);
            }
        }

        // Xử lý một đơn; false khi phải dừng chu kỳ
        private async Task<bool> ReportAsync(Booking booking, DateTime now, Cycle cycle)
        {
            if (booking.TadssDecisionId is not { } decisionId)
            {
                // Truy vấn chỉ chọn đơn có quyết định; gặp thì là dữ liệu đổi giữa chừng
                cycle.Invalid++;
                _logger.LogError("[Outcome] Booking {BookingId} không có tadss_decision_id; không gửi outcome", booking.Id);
                return true;
            }

            var plan = TadssOutcomeMapper.Plan(booking, now);
            switch (plan.Kind)
            {
                case TadssOutcomePlanKind.NotDue:
                    cycle.NotDue++;
                    return true;

                case TadssOutcomePlanKind.Skip:
                    await MarkAsync(booking.Id, now, cycle);
                    cycle.Skipped++;
                    _logger.LogWarning(
                        "[Outcome] Booking {BookingId} (quyết định {DecisionId}) hủy do SYSTEM_CLEANUP: không gửi TADSS, đánh dấu đã xử lý",
                        booking.Id, decisionId);
                    return true;

                case TadssOutcomePlanKind.Invalid:
                    // Không đoán nhãn: giữ lại để người vận hành sửa dữ liệu, chu kỳ sau tự gửi
                    cycle.Invalid++;
                    _logger.LogError(
                        "[Outcome] Booking {BookingId} (quyết định {DecisionId}) có dữ liệu lệch, không gửi outcome: {Problem}",
                        booking.Id, decisionId, plan.Problem);
                    return true;
            }

            if (plan.Request is not { } request)
                return true;

            var result = await _tadssClient.ReportOutcomeAsync(decisionId, request);
            switch (TadssOutcomeDelivery.Classify(result))
            {
                case TadssOutcomeDeliveryKind.Recorded:
                    if (!result.IsSuccess)
                        _logger.LogWarning(
                            "[Outcome] TADSS đã có outcome khác cho quyết định {DecisionId} (booking {BookingId}) ({Code}); outcome BE vừa gửi: " +
                            "{Outcome} lúc {OccurredAt:O}. Đánh dấu đã báo",
                            decisionId, booking.Id, result.ErrorCode, request.Outcome, request.OccurredAt);
                    await MarkAsync(booking.Id, now, cycle);
                    cycle.Recorded++;
                    return true;

                case TadssOutcomeDeliveryKind.Rejected:
                    cycle.Rejected++;
                    _logger.LogWarning(
                        "[Outcome] TADSS từ chối outcome của booking {BookingId} (quyết định {DecisionId}): HTTP {Status} {Code}; để chu kỳ sau",
                        booking.Id, decisionId, result.HttpStatus, result.ErrorCode);
                    return true;

                default:
                    cycle.StopReason = result.FailureKind == TadssFailureKind.Http
                        ? $"HTTP {result.HttpStatus} {result.ErrorCode}".TrimEnd()
                        : result.FailureKind.ToString();
                    _logger.LogWarning(
                        "[Outcome] Lỗi hệ thống khi gửi outcome của booking {BookingId} (quyết định {DecisionId}): {Reason}, Retry-After={RetryAfter}. " +
                        "Dừng chu kỳ, chu kỳ sau thử lại. CorrelationId={CorrelationId}",
                        booking.Id, decisionId, cycle.StopReason, result.RetryAfter, result.CorrelationId);
                    return false;
            }
        }

        private async Task MarkAsync(Guid bookingId, DateTime now, Cycle cycle)
        {
            if (await _bookingRepo.MarkOutcomeReportedAsync(bookingId, now))
                cycle.MarkedIds.Add(bookingId);
            else
                // Lần chạy khác (instance khác) đã đánh dấu trước: TADSS chống trùng theo nội dung nên không có gì sai
                _logger.LogInformation("[Outcome] Booking {BookingId} đã được đánh dấu bởi lần chạy khác", bookingId);
        }

        // Một dòng có cấu trúc mỗi chu kỳ; tuổi đơn chưa báo cũ nhất (theo updated_at) giúp phát hiện đơn bị kẹt sau các đơn bị từ chối
        private void LogSummary(IReadOnlyList<Booking> candidates, Cycle cycle, DateTime now, long elapsedMs)
        {
            var unreported = candidates.Where(b => !cycle.MarkedIds.Contains(b.Id)).ToList();
            var oldestAge = unreported.Count == 0 ? "-" : FormatAge(now - unreported.Min(b => b.UpdatedAt));

            _logger.LogInformation(
                "[Outcome] Chu kỳ {Candidates} ứng viên: đã báo {Recorded}, bỏ qua {Skipped}, chưa tới giờ {NotDue}, lệch dữ liệu {Invalid}, " +
                "bị từ chối {Rejected}, dừng: {StopReason}, tuổi đơn chưa báo cũ nhất {OldestAge}. Thời gian chạy: {Ms}ms",
                candidates.Count, cycle.Recorded, cycle.Skipped, cycle.NotDue, cycle.Invalid, cycle.Rejected, cycle.StopReason ?? "không",
                oldestAge, elapsedMs);
        }

        private static string FormatAge(TimeSpan age) =>
            age < TimeSpan.Zero ? "0d00h00m" : $"{(int)age.TotalDays}d{age.Hours:00}h{age.Minutes:00}m";

        private sealed class Cycle
        {
            public int Recorded { get; set; }
            public int Skipped { get; set; }
            public int NotDue { get; set; }
            public int Invalid { get; set; }
            public int Rejected { get; set; }
            public string? StopReason { get; set; }
            public HashSet<Guid> MarkedIds { get; } = [];
        }
    }
}
