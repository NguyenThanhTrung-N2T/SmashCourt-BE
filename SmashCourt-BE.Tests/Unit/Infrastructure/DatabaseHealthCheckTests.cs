using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmashCourt_BE.Data;
using SmashCourt_BE.Infrastructure.HealthChecks;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Infrastructure;

/// <summary>
/// OPS-04: <c>/health/ready</c> phải báo không sẵn sàng khi BE không kết nối được PostgreSQL (ca DB sống đã kiểm ở <c>HealthEndpointTests</c>),
/// và kết quả không mang chuỗi kết nối hay host (body của health check có thể bị đọc công khai).
/// </summary>
[Trait("Category", TestCategories.Configuration)]
[Trait("Catalog", "OPS-04")]
public class DatabaseHealthCheckTests
{
    private const string Password = "mat-khau-khong-duoc-lo-ra";

    [Fact]
    public async Task CheckHealth_WhenDatabaseUnreachable_IsUnhealthyWithoutConnectionDetails()
    {
        // Cổng 9 (discard) trên loopback: kết nối bị từ chối ngay, không ra mạng ngoài
        var options = new DbContextOptionsBuilder<SmashCourtContext>()
            .UseNpgsql($"Host=127.0.0.1;Port=9;Database=smashcourt;Username=u;Password={Password};Timeout=3")
            .Options;
        await using var context = new SmashCourtContext(options);
        var check = new DatabaseHealthCheck(context);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(Password, result.Description);
        Assert.DoesNotContain("127.0.0.1", result.Description);
    }
}
