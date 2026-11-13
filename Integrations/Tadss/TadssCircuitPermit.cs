namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Quyền gọi TADSS cho một lần đánh giá, do <see cref="ITadssCircuit.TryAcquire"/> cấp. Đơn thử (<see cref="IsTrial"/>) chỉ được gọi một lần;
/// lượt thử phải trả lại bằng <see cref="ITadssCircuit.Release"/> trong <c>finally</c> để mạch không kẹt ở nửa mở khi đơn thử bị hủy.
/// </summary>
public sealed class TadssCircuitPermit
{
    /// <summary>Quyền khi mạch tắt (<c>CircuitEnabled</c> hoặc cờ TADSS tắt): hành vi đúng như trước P6-57, mạch không ghi nhận gì.</summary>
    public static TadssCircuitPermit Untracked { get; } = new(isTrial: false, tracked: false);

    internal TadssCircuitPermit(bool isTrial, bool tracked)
    {
        IsTrial = isTrial;
        Tracked = tracked;
    }

    /// <summary>Đơn thử của trạng thái nửa mở.</summary>
    public bool IsTrial { get; }

    /// <summary>Mạch có ghi nhận kết quả của lần gọi này.</summary>
    public bool Tracked { get; }

    /// <summary>Lượt thử đã có kết quả hoặc đã trả lại (chỉ dùng cho đơn thử).</summary>
    public bool Resolved { get; internal set; }
}
