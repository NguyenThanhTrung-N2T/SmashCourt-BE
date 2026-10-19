using SmashCourt_BE.Common;

namespace SmashCourt_BE.Tests.Helpers;

/// <summary>
/// Custom assertions for AppException to simplify test code and ensure consistent validation.
/// </summary>
public static class AppExceptionAssertions
{
    /// <summary>
    /// Asserts that the exception is an AppException with the expected status code, error code, and optional message content.
    /// </summary>
    /// <param name="exception">The exception to assert.</param>
    /// <param name="statusCode">Expected HTTP status code.</param>
    /// <param name="errorCode">Expected error code.</param>
    /// <param name="messageContains">Optional substring that should be present in the error message.</param>
    public static void ShouldBeAppException(
        this Exception exception,
        int statusCode,
        string errorCode,
        string? messageContains = null)
    {
        var appException = Assert.IsType<AppException>(exception);
        Assert.Equal(statusCode, appException.StatusCode);
        Assert.Equal(errorCode, appException.ErrorCode);
        
        if (messageContains is not null)
        {
            Assert.Contains(messageContains, appException.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Asserts that the task throws an AppException with the expected properties.
    /// </summary>
    public static async Task<AppException> ShouldThrowAppExceptionAsync(
        this Task task,
        int statusCode,
        string errorCode,
        string? messageContains = null)
    {
        var exception = await Assert.ThrowsAsync<AppException>(() => task);
        exception.ShouldBeAppException(statusCode, errorCode, messageContains);
        return exception;
    }

    /// <summary>
    /// Asserts that the function throws an AppException with the expected properties.
    /// </summary>
    public static async Task<AppException> ShouldThrowAppExceptionAsync<T>(
        this Func<Task<T>> func,
        int statusCode,
        string errorCode,
        string? messageContains = null)
    {
        var exception = await Assert.ThrowsAsync<AppException>(func);
        exception.ShouldBeAppException(statusCode, errorCode, messageContains);
        return exception;
    }
}
