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

    /// <summary>
    /// Lỗi theo trường (tên thuộc tính C# → danh sách thông báo), cùng dạng <c>errors</c> của lỗi validate MVC.
    /// Null với lỗi nghiệp vụ thường.
    /// </summary>
    public Dictionary<string, string[]>? Errors { get; }

    public AppException(
        int statusCode,
        string message,
        string errorCode = ErrorCodes.BadRequest,
        Dictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode  = errorCode;
        Errors     = errors;
    }
}