namespace SmashCourt_BE.Tests.Integration.Invariants;

/// <summary>Bộ tự kiểm SC6 sau test HTTP thấy vi phạm (plan 6A-6 Task 10). Tiền tố <c>[SC6]</c> để tách khỏi lỗi của thân test.</summary>
public sealed class InvariantViolationException(IReadOnlyList<InvariantViolation> violations) : Exception(
    $"[SC6] {violations.Count} vi phạm bất biến booking sau test:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}")
{
    public IReadOnlyList<InvariantViolation> Violations { get; } = violations;
}
