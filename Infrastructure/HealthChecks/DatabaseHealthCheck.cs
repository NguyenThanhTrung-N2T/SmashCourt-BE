using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmashCourt_BE.Data;

namespace SmashCourt_BE.Infrastructure.HealthChecks;

/// <summary>
/// Kiểm BE kết nối được PostgreSQL (điều kiện sẵn sàng của <c>/health/ready</c>, Epic 6C OPS-04). Không đưa chi tiết lỗi (chuỗi kết nối, host) vào kết quả.
/// </summary>
public sealed class DatabaseHealthCheck(SmashCourtContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Không kết nối được PostgreSQL");
        }
    }
}
