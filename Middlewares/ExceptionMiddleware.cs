using SmashCourt_BE.Common;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmashCourt_BE.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    // Dùng lại JsonSerializerOptions — không tạo mới mỗi request
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            // Lỗi nghiệp vụ — log info, trả về message + code rõ ràng
            _logger.LogInformation(
                "Business error {StatusCode} [{ErrorCode}]: {Message}",
                ex.StatusCode, ex.ErrorCode, ex.Message);

            await WriteResponseAsync(context, ex.StatusCode,
                ApiResponse.Fail(ex.Message, ex.ErrorCode));
        }
        catch (UnauthorizedAccessException ex)
        {
            // Không có quyền truy cập — 401
            _logger.LogWarning("Unauthorized access: {Message}", ex.Message);

            await WriteResponseAsync(context, StatusCodes.Status401Unauthorized,
                ApiResponse.Fail("Bạn chưa xác thực hoặc không có quyền truy cập", ErrorCodes.Unauthorized));
        }
        catch (KeyNotFoundException ex)
        {
            // Resource không tồn tại — 404
            _logger.LogInformation("Resource not found: {Message}", ex.Message);

            await WriteResponseAsync(context, StatusCodes.Status404NotFound,
                ApiResponse.Fail(ex.Message, ErrorCodes.NotFound));
        }
        catch (OperationCanceledException)
        {
            // Client ngắt kết nối — bỏ qua, không log error
            _logger.LogDebug("Request cancelled by client");
        }
        catch (Exception ex)
        {
            // Lỗi hệ thống không xác định — log đầy đủ, không leak detail ra client
            _logger.LogError(ex, "Unhandled exception at {Path}", context.Request.Path);

            await WriteResponseAsync(context, StatusCodes.Status500InternalServerError,
                ApiResponse.Fail("Đã xảy ra lỗi hệ thống, vui lòng thử lại sau", ErrorCodes.InternalError));
        }
    }

    private static async Task WriteResponseAsync<T>(HttpContext context, int statusCode, ApiResponse<T> body)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(body, _jsonOptions));
    }
}