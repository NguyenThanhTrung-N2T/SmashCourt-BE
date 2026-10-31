using SmashCourt_BE.Integrations.Tadss.DTOs;

namespace SmashCourt_BE.Integrations.Tadss;

/// <summary>
/// Kết quả ánh xạ một booking sang outcome: <see cref="Request"/> chỉ có khi <see cref="TadssOutcomePlanKind.Send"/>,
/// <see cref="Problem"/> chỉ có khi <see cref="TadssOutcomePlanKind.Invalid"/> (nêu rõ lệch gì để người vận hành sửa dữ liệu).
/// </summary>
public sealed record TadssOutcomePlan(TadssOutcomePlanKind Kind, TadssOutcomeRequestDto? Request, string? Problem)
{
    public static TadssOutcomePlan Send(TadssOutcome outcome, DateTimeOffset occurredAt) =>
        new(TadssOutcomePlanKind.Send, new TadssOutcomeRequestDto { Outcome = outcome, OccurredAt = occurredAt }, null);

    public static TadssOutcomePlan NotDue { get; } = new(TadssOutcomePlanKind.NotDue, null, null);

    public static TadssOutcomePlan Skip { get; } = new(TadssOutcomePlanKind.Skip, null, null);

    public static TadssOutcomePlan Invalid(string problem) => new(TadssOutcomePlanKind.Invalid, null, problem);
}
