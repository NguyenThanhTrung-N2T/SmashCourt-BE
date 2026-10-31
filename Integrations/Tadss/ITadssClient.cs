using SmashCourt_BE.Integrations.Tadss.DTOs;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Client gọi TADSS Decision Runtime v2. Lỗi của TADSS (timeout, mạng, HTTP, body hỏng) trả về trong
/// <see cref="TadssCallResult{T}"/>, không ném exception; chỉ việc caller hủy <c>CancellationToken</c> mới ném
/// <see cref="OperationCanceledException"/> (không bị tính là timeout, không thử lại).
/// </summary>
public interface ITadssClient
{
    /// <summary>Đánh giá cọc cho booking. Thử lại đúng một lần (cùng idempotency key) khi timeout, lỗi mạng hoặc 5xx.</summary>
    Task<TadssCallResult<TadssDecisionResultDto>> EvaluateAsync(
        TadssDecisionRequestDto request, Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>Manager duyệt hoặc từ chối quyết định NEED_APPROVAL. Không thử lại.</summary>
    Task<TadssCallResult<TadssOverrideResponseDto>> OverrideAsync(
        Guid decisionId, TadssOverrideRequestDto request, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Đọc chi tiết quyết định (đối soát). Không thử lại.</summary>
    Task<TadssCallResult<TadssDecisionDetailDto>> GetDecisionAsync(
        Guid decisionId, CancellationToken cancellationToken = default);

    /// <summary>Báo kết quả thực tế của booking về TADSS. Không thử lại (job gọi lại ở chu kỳ sau).</summary>
    Task<TadssCallResult<TadssOutcomeResponseDto>> ReportOutcomeAsync(
        Guid decisionId, TadssOutcomeRequestDto request, CancellationToken cancellationToken = default);
}
