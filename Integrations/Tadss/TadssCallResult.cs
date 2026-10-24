using System.Diagnostics.CodeAnalysis;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Kết quả một lần gọi TADSS: thành công kèm <typeparamref name="T"/>, hoặc thất bại kèm loại lỗi.
/// Client không ném exception cho lỗi của TADSS; service quyết định ánh xạ sang nghiệp vụ.
/// </summary>
public sealed class TadssCallResult<T>
{
    private TadssCallResult() { }

    /// <summary>Mã đối soát gửi trong <c>X-Correlation-ID</c> (khớp log hai phía).</summary>
    public required string CorrelationId { get; init; }

    public T? Value { get; private init; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(FailureKind))]
    public bool IsSuccess { get; private init; }

    public TadssFailureKind? FailureKind { get; private init; }

    /// <summary>Mã HTTP nhận được (có ở thành công, <c>Http</c> và <c>InvalidResponse</c>).</summary>
    public int? HttpStatus { get; private init; }

    /// <summary>Trường <c>code</c> của RFC 7807; null với 415 của MVC hoặc body không phải problem+json.</summary>
    public string? ErrorCode { get; private init; }

    /// <summary>Thời gian nên chờ theo <c>Retry-After</c> (429, 503).</summary>
    public TimeSpan? RetryAfter { get; private init; }

    /// <summary>
    /// Tên các trường bị lỗi trong <c>errors</c> của RFC 7807 (đường dẫn JSON như <c>deposit.rate</c>, <c>reason</c>); rỗng nếu không có.
    /// Duyệt cọc dùng để tách lỗi tỷ lệ do Quản lý nhập (400) với lỗi do BE gửi sai (500).
    /// </summary>
    public IReadOnlyList<string> ErrorFields { get; private init; } = [];

    public static TadssCallResult<T> Success(T value, int httpStatus, string correlationId) => new()
    {
        CorrelationId = correlationId,
        IsSuccess = true,
        Value = value,
        HttpStatus = httpStatus
    };

    public static TadssCallResult<T> Failure(
        TadssFailureKind kind,
        string correlationId,
        int? httpStatus = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null,
        IReadOnlyList<string>? errorFields = null) => new()
    {
        CorrelationId = correlationId,
        IsSuccess = false,
        FailureKind = kind,
        HttpStatus = httpStatus,
        ErrorCode = errorCode,
        RetryAfter = retryAfter,
        ErrorFields = errorFields ?? []
    };
}
