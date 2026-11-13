namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>Kiểm <c>GET /health/ready</c> của TADSS cho mạch ngắt (P6-57). Không ném lỗi của TADSS; chỉ hủy của caller được lan ra.</summary>
public interface ITadssHealthProbe
{
    // true khi TADSS trả 2xx trong CircuitProbeTimeoutMs
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
