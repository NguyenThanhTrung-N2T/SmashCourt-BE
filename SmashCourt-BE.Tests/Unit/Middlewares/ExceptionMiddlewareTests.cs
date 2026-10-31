using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SmashCourt_BE.Common;
using SmashCourt_BE.Middlewares;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Middlewares;

/// <summary>
/// Envelope lỗi do <see cref="ExceptionMiddleware"/> dựng. Phase 6A-3: lỗi nghiệp vụ mang được danh sách lỗi theo trường
/// (bước 4b của duyệt cọc trả 400 <c>errors.DepositRate</c> giống hệt lỗi validate của MVC).
/// </summary>
[TestCategory(TestCategories.Validation)]
[Trait("Catalog", "BE-UT-03")]
public class ExceptionMiddlewareTests
{
    private static async Task<(int Status, JsonElement Body)> InvokeAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(_ => throw exception, NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, document.RootElement.Clone());
    }

    [Fact]
    public async Task AppExceptionWithFieldErrors_WritesErrorsWithPropertyNamesKept()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["DepositRate"] = ["Số tiền cọc tối thiểu 5.000 đ, hoặc chọn 0 %"]
        };

        var (status, body) = await InvokeAsync(
            new AppException(400, "Dữ liệu đầu vào không hợp lệ", ErrorCodes.ValidationError, errors));

        Assert.Equal(400, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
        Assert.Equal("Dữ liệu đầu vào không hợp lệ", body.GetProperty("message").GetString());
        var depositRate = body.GetProperty("errors").GetProperty("DepositRate");
        Assert.Equal("Số tiền cọc tối thiểu 5.000 đ, hoặc chọn 0 %", Assert.Single(depositRate.EnumerateArray()).GetString());
    }

    [Fact]
    public async Task AppExceptionWithoutFieldErrors_KeepsErrorsNull()
    {
        var (status, body) = await InvokeAsync(new AppException(409, "Đơn đã được xử lý", ErrorCodes.ApprovalAlreadyResolved));

        Assert.Equal(409, status);
        Assert.Equal("APPROVAL_ALREADY_RESOLVED", body.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("errors").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
    }

    [Fact]
    public void AppException_DefaultsToNoFieldErrors()
    {
        Assert.Null(new AppException(400, "x").Errors);
    }
}
