using System.Diagnostics;

namespace SmashCourt_BE.Jobs
{
    /// <summary>
    /// Vòng hủy theo lô dùng chung của job hết hạn thanh toán (Job-01) và job hết hạn duyệt: lấy id đơn quá hạn, hủy từng đơn qua dịch vụ
    /// hủy chung (mỗi đơn một transaction riêng, có điều kiện); một đơn lỗi không chặn đơn khác, lần chạy sau thử lại; log tổng đủ bốn số.
    /// Không ném exception ra Hangfire.
    /// </summary>
    internal static class ExpiryBatch
    {
        public static async Task RunAsync(
            string job, Func<Task<List<Guid>>> loadExpiredIds, Func<Guid, Task<bool>> expire, ILogger logger)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var ids = await loadExpiredIds();
                if (ids.Count == 0)
                {
                    logger.LogInformation("[{Job}] Không có đơn nào quá hạn. Thời gian chạy: {Ms}ms", job, sw.ElapsedMilliseconds);
                    return;
                }

                int cancelled = 0, skipped = 0, failed = 0;
                foreach (var id in ids)
                {
                    try
                    {
                        // false: đơn không còn quá hạn khi đã khóa (vừa gia hạn / vừa duyệt) hoặc đã đổi trạng thái
                        if (await expire(id))
                            cancelled++;
                        else
                            skipped++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        logger.LogError(ex, "[{Job}] Hủy booking {BookingId} quá hạn lỗi", job, id);
                    }
                }

                logger.LogInformation(
                    "[{Job}] {Candidates} ứng viên: đã hủy {Cancelled}, bỏ qua {Skipped}, lỗi {Failed}. Thời gian chạy: {Ms}ms",
                    job, ids.Count, cancelled, skipped, failed, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[{Job}] Gặp lỗi không mong muốn. Thời gian chạy: {Ms}ms", job, sw.ElapsedMilliseconds);
            }
        }
    }
}
