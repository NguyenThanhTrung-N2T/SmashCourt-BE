namespace SmashCourt_BE.Common;

/// <summary>
/// Custom exception — throw lỗi nghiệp vụ kèm HTTP status code và machine-readable error code.
/// </summary>
public class AppException : Exception
{
    public int StatusCode { get; }

    /// <summary>
    /// Machine-readable code — dùng hằng số từ <see cref="ErrorCodes"/>.
    /// Mặc định là "BAD_REQUEST" nếu không truyền.
    /// </summary>
    public string ErrorCode { get; }

    public AppException(int statusCode, string message, string errorCode = ErrorCodes.BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode  = errorCode;
    }
}