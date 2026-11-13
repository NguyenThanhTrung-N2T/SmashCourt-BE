namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Mạch ngắt của lệnh đánh giá cọc (P6-57), chỉ <see cref="TadssClient.EvaluateAsync"/> dùng. Lệnh duyệt / từ chối, chi tiết quyết định và
/// outcome không đọc, không ghi trạng thái mạch.
/// </summary>
public interface ITadssCircuit
{
    // Trạng thái hiện tại (đọc để log, metric)
    TadssCircuitState State { get; }

    // Xin quyền gọi; null = mạch đang mở (hoặc đã có đơn thử đang chạy): đơn đi phương án dự phòng ngay
    TadssCircuitPermit? TryAcquire();

    // Được gọi lần thử lại không: đơn thử không thử lại; mạch vừa mở trong lúc đơn đang chờ thì bỏ lần thử lại
    bool CanRetry(TadssCircuitPermit permit);

    // Ghi kết quả của một lần gọi (mỗi lần thử tính một)
    void RecordAttempt(TadssCircuitPermit permit, TadssAttemptOutcome outcome);

    // Trả lượt thử nếu đơn thử kết thúc mà chưa có kết quả (bị hủy, ném lỗi); gọi trong finally
    void Release(TadssCircuitPermit permit);
}
