namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>
/// Test chỉ chạy khi có <c>SC6_CONNECTION_STRING</c> (lượt kiểm SC6 trên staging, Phase 7); lượt chạy thường hiện là Skipped.
/// Không cần package mới: đặt <see cref="FactAttribute.Skip"/> lúc dựng attribute.
/// </summary>
public sealed class StagingFactAttribute : FactAttribute
{
    public StagingFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(StagingRunOptions.ConnectionStringVariable)))
            Skip = $"Chỉ chạy trên staging: đặt {StagingRunOptions.ConnectionStringVariable}, SC6_FROM_UTC, SC6_TO_UTC (plan 6A-6 Task 11).";
    }
}
