using SmashCourt_BE.Common;

namespace SmashCourt_BE.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

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
            // Lỗi nghiệp vụ — log info, trả về message rõ ràng cho client
            _logger.LogInformation("Business error {StatusCode}: {Message}", ex.StatusCode, ex.Message);

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("The response has already started, the ExceptionMiddleware will not be executed.");
                throw;
            }

            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            // Lỗi hệ thống — log error đầy đủ, không leak detail ra client
            _logger.LogError(ex, "Unhandled exception");

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("The response has already started, the ExceptionMiddleware will not be executed.");
                throw;
            }

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                message = "Đã xảy ra lỗi hệ thống, vui lòng thử lại sau"
            });
        }
    }
}