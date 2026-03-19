namespace SmashCourt_BE.Common;

// Custom exception — dùng để throw lỗi nghiệp vụ kèm HTTP status code
public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}