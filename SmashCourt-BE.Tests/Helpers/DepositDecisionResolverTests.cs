using Microsoft.Extensions.Logging;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Integrations.Tadss;
using SmashCourt_BE.Integrations.Tadss.DTOs;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Helpers;

/// <summary>
/// DepositDecisionResolver (spec 7.6, D5): thứ tự chọn nhánh, ranh giới mức tối thiểu cho cả tiền cọc lẫn tổng tiền dự phòng,
/// REJECTED/NEED_APPROVAL không bao giờ bị ép sang xác nhận chỉ vì số tiền nhỏ.
/// </summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Category", TestCategories.Tadss)]
public class DepositDecisionResolverTests
{
    private const decimal Min = 5000m;
    private static readonly Guid DecisionId = Guid.Parse("0f9c7e5a-0000-4000-8000-00000000d003");

    private static TadssDecisionResultDto Result(
        TadssDecisionStatus status, decimal? depositAmount = null, decimal depositRate = 0.2m, DateTimeOffset? approvalExpiresAt = null,
        bool fallbackUsed = false, string reasonCode = "TIER_DEPOSIT", string[]? reasons = null) => new()
    {
        DecisionId = DecisionId, ReservationId = Guid.NewGuid(), Status = status,
        Deposit = depositAmount == null ? null : new TadssDepositDto { Rate = depositRate, Amount = depositAmount.Value, Currency = "VND" },
        StrategyName = "DefaultDecisionStrategy", StrategyVersion = "2.1.0", ExecutionType = "AUTOMATED", FallbackUsed = fallbackUsed,
        ReasonCode = reasonCode, Reasons = reasons?.ToList() ?? [], Recommendations = [],
        ApprovalExpiresAt = approvalExpiresAt, EvaluatedAt = DateTimeOffset.Parse("2026-11-20T10:00:00+00:00")
    };

    private static TadssCallResult<TadssDecisionResultDto> Ok(TadssDecisionResultDto value) =>
        TadssCallResult<TadssDecisionResultDto>.Success(value, 200, "corr");

    private static TadssCallResult<TadssDecisionResultDto> Approved(decimal depositAmount, decimal rate = 0.2m, bool fallbackUsed = false) =>
        Ok(Result(TadssDecisionStatus.APPROVED, depositAmount, rate, fallbackUsed: fallbackUsed));

    private static TadssCallResult<TadssDecisionResultDto> NeedApproval() =>
        Ok(Result(TadssDecisionStatus.NEED_APPROVAL, approvalExpiresAt: DateTimeOffset.Parse("2026-11-20T20:30:00+07:00"),
            reasonCode: "HIGH_VALUE_REVIEW", reasons: ["Đơn giá trị lớn"]));

    private static TadssCallResult<TadssDecisionResultDto> Rejected() =>
        Ok(Result(TadssDecisionStatus.REJECTED, reasonCode: "REPEATED_PAYMENT_EXPIRY", reasons: ["Hay giữ chỗ không trả"]));

    private static TadssCallResult<TadssDecisionResultDto> Failure(TadssFailureKind kind, int? http = null) =>
        TadssCallResult<TadssDecisionResultDto>.Failure(kind, "corr-x", http, http == null ? null : "SOME_CODE");

    public static TheoryData<decimal> AmountsAroundMinimum => new() { 0m, 1m, 4999m, 4999.99m, 5000m, 5000.01m, 200_000m };

    public static TheoryData<decimal> TotalsAroundMinimum => new() { 0m, 4999m, 5000m, 5001m, 200_000m };

    // ───────────────────────── F1: mức cọc TADSS không khớp đơn ─────────────────────────

    [Theory]
    [Trait("Catalog", "BE-IT-04")]
    [InlineData(200_001)]
    [InlineData(400_000)]
    public void Resolve_WhenApprovedDepositExceedsOrderTotal_FallsBackToFullPaymentWithError(decimal depositAmount)
    {
        // TADSS lỗi tính tiền: không được thu online nhiều hơn tổng đơn
        var decision = DepositDecisionResolver.Resolve(Approved(depositAmount, 1m), 200_000m, Min);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Equal(200_000m, decision.DepositAmount);
        Assert.Null(decision.TadssDecisionId);
        Assert.Equal(LogLevel.Error, decision.LogLevel);
        Assert.Contains(DecisionId.ToString(), decision.LogMessage);
    }

    [Fact]
    public void Resolve_WhenApprovedDepositEqualsOrderTotal_KeepsTadssDeposit()
    {
        var decision = DepositDecisionResolver.Resolve(Approved(200_000m, 1m), 200_000m, Min);

        Assert.Equal(DepositBranch.TadssDeposit, decision.Branch);
        Assert.Equal(200_000m, decision.DepositAmount);
        Assert.Equal(DecisionId, decision.TadssDecisionId);
    }

    [Theory]
    [Trait("Catalog", "BE-IT-04")]
    [InlineData("USD")]
    [InlineData("vnd")]
    public void Resolve_WhenApprovedDepositCurrencyIsNotVnd_FallsBackToFullPaymentWithError(string currency)
    {
        var result = Result(TadssDecisionStatus.APPROVED, 40_000m);
        result.Deposit!.Currency = currency;

        var decision = DepositDecisionResolver.Resolve(Ok(result), 200_000m, Min);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(LogLevel.Error, decision.LogLevel);
        Assert.Contains(currency, decision.LogMessage);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-24")]
    public void Resolve_WhenApprovedDepositExceedsSmallOrderTotal_ConfirmsWithoutOnlinePaymentAndKeepsError()
    {
        var decision = DepositDecisionResolver.Resolve(Approved(4_500m, 1m), 4_000m, Min);

        Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Equal(LogLevel.Error, decision.LogLevel);
    }

    // ───────────────────────── (4)–(5) APPROVED: tiền cọc so với mức tối thiểu ─────────────────────────

    [Fact]
    [Trait("Catalog", "BE-IT-24")]
    public void Resolve_WhenApprovedDepositBelowMinimumOnLargeOrder_ConfirmsWithoutOnlinePaymentAndKeepsTadssSource()
    {
        // Z-01: đơn 200.000 đ, TADSS cọc 2 % = 4.000 đ: đơn không miễn phí, chỉ không thu cọc online
        var decision = DepositDecisionResolver.Resolve(Approved(4_000m, 0.02m), 200_000m, Min);

        Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, decision.Branch);
        Assert.Equal(DepositSource.TADSS, decision.DepositSource);
        Assert.Equal(0m, decision.DepositRate);
        Assert.Equal(0m, decision.DepositAmount);
        Assert.Equal(4_000m, decision.OnlineAmount);
        Assert.Equal(DecisionId, decision.TadssDecisionId);
        Assert.Equal(LogLevel.Warning, decision.LogLevel);
        Assert.Contains("4000", decision.LogMessage);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-02")]
    public void Resolve_WhenApprovedDepositEqualsMinimum_RequiresPaymentWithTadssAmount()
    {
        // Z-02
        var decision = DepositDecisionResolver.Resolve(Approved(5_000m, 0.025m), 200_000m, Min);

        Assert.Equal(DepositBranch.TadssDeposit, decision.Branch);
        Assert.Equal(DepositSource.TADSS, decision.DepositSource);
        Assert.Equal(0.025m, decision.DepositRate);
        Assert.Equal(5_000m, decision.DepositAmount);
        Assert.Equal(5_000m, decision.OnlineAmount);
        Assert.Equal(DecisionId, decision.TadssDecisionId);
        Assert.Equal(LogLevel.None, decision.LogLevel);
        Assert.Null(decision.LogMessage);
    }

    [Fact]
    [Trait("Catalog", "BE-IT-24")]
    public void Resolve_WhenApprovedZeroDepositOnZeroOrder_ConfirmsWithoutOnlinePayment()
    {
        // Z-03
        var decision = DepositDecisionResolver.Resolve(Approved(0m, 0m), 0m, Min);

        Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, decision.Branch);
        Assert.Equal(DepositSource.TADSS, decision.DepositSource);
        Assert.Equal(DecisionId, decision.TadssDecisionId);
    }

    [Theory]
    [MemberData(nameof(AmountsAroundMinimum))]
    public void Resolve_WhenApproved_BranchFollowsDepositAmountNotOrderTotal(decimal depositAmount)
    {
        var decision = DepositDecisionResolver.Resolve(Approved(depositAmount), 1_000_000m, Min);

        var expected = depositAmount < Min ? DepositBranch.ConfirmWithoutOnlinePayment : DepositBranch.TadssDeposit;
        Assert.Equal(expected, decision.Branch);
        Assert.Equal(depositAmount, decision.OnlineAmount);
        Assert.Equal(depositAmount < Min ? 0m : depositAmount, decision.DepositAmount);
    }

    [Theory]
    [InlineData(5_000, 0, true)]
    [InlineData(10_000, 5_000, true)]
    [InlineData(10_000, 9_999, true)]
    [InlineData(10_000, 10_000, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, false)]
    public void Resolve_WhenMinimumIsConfigured_UsesTheGivenMinimumNotAHardcodedValue(int min, int deposit, bool belowMinimum)
    {
        var decision = DepositDecisionResolver.Resolve(Approved(deposit), 500_000m, min);

        Assert.Equal(belowMinimum ? DepositBranch.ConfirmWithoutOnlinePayment : DepositBranch.TadssDeposit, decision.Branch);
    }

    [Theory]
    [InlineData(4_999, DepositBranch.ConfirmWithoutOnlinePayment)]
    [InlineData(5_000, DepositBranch.TadssDeposit)]
    [Trait("Catalog", "BE-IT-02")]
    public void Resolve_WhenRuntimeFallbackApproves100PercentDeposit_IsStillTadssSource(int total, DepositBranch expected)
    {
        // Z-08: fallback của runtime TADSS (fallbackUsed) và APPROVAL_WINDOW_UNAVAILABLE cọc 100 % vẫn là quyết định TADSS, không phải nhánh d
        foreach (var fallbackUsed in new[] { true, false })
        {
            var decision = DepositDecisionResolver.Resolve(Approved(total, 1m, fallbackUsed), total, Min);

            Assert.Equal(expected, decision.Branch);
            Assert.Equal(DepositSource.TADSS, decision.DepositSource);
            Assert.Equal(DecisionId, decision.TadssDecisionId);
        }
    }

    // ───────────────────────── (1) cờ tắt ─────────────────────────

    [Theory]
    [MemberData(nameof(TotalsAroundMinimum))]
    [Trait("Catalog", "BE-IT-04")]
    public void Resolve_WhenTadssWasNotCalled_ChargesFullTotalOnlyIfAtLeastMinimum(decimal total)
    {
        // Z-04: cờ tắt
        var decision = DepositDecisionResolver.Resolve(null, total, Min);

        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Null(decision.TadssDecisionId);
        Assert.Equal(total, decision.OnlineAmount);
        if (total >= Min)
        {
            Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
            Assert.Equal(1m, decision.DepositRate);
            Assert.Equal(total, decision.DepositAmount);
            Assert.Equal(LogLevel.None, decision.LogLevel);
        }
        else
        {
            Assert.Equal(DepositBranch.ConfirmWithoutOnlinePayment, decision.Branch);
            Assert.Equal(0m, decision.DepositRate);
            Assert.Equal(0m, decision.DepositAmount);
            Assert.Equal(LogLevel.Warning, decision.LogLevel);
        }
    }

    // ───────────────────────── (2) TADSS lỗi ─────────────────────────

    [Theory]
    [InlineData(TadssFailureKind.Timeout, null, 4_999, DepositBranch.ConfirmWithoutOnlinePayment)]
    [InlineData(TadssFailureKind.Timeout, null, 5_000, DepositBranch.FullPaymentFallback)]
    [InlineData(TadssFailureKind.Network, null, 4_999, DepositBranch.ConfirmWithoutOnlinePayment)]
    [InlineData(TadssFailureKind.Network, null, 5_000, DepositBranch.FullPaymentFallback)]
    [InlineData(TadssFailureKind.Http, 503, 4_999, DepositBranch.ConfirmWithoutOnlinePayment)]
    [InlineData(TadssFailureKind.Http, 503, 5_000, DepositBranch.FullPaymentFallback)]
    [InlineData(TadssFailureKind.InvalidResponse, 200, 4_999, DepositBranch.ConfirmWithoutOnlinePayment)]
    [InlineData(TadssFailureKind.InvalidResponse, 200, 5_000, DepositBranch.FullPaymentFallback)]
    [Trait("Catalog", "BE-IT-04")]
    public void Resolve_WhenTadssFails_FallsBackToFullPaymentWithoutDecisionId(TadssFailureKind kind, int? http, int total, DepositBranch expected)
    {
        // Z-05
        var decision = DepositDecisionResolver.Resolve(Failure(kind, http), total, Min);

        Assert.Equal(expected, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Null(decision.TadssDecisionId);
        Assert.Equal(LogLevel.Warning, decision.LogLevel);
        Assert.Contains(kind.ToString(), decision.LogMessage);
        Assert.Contains("corr-x", decision.LogMessage);
    }

    [Theory]
    [InlineData(400, LogLevel.Error)]
    [InlineData(409, LogLevel.Error)]
    [InlineData(401, LogLevel.Warning)]
    [InlineData(413, LogLevel.Warning)]
    [InlineData(429, LogLevel.Warning)]
    [InlineData(500, LogLevel.Warning)]
    [InlineData(502, LogLevel.Warning)]
    [InlineData(503, LogLevel.Warning)]
    [InlineData(504, LogLevel.Warning)]
    [InlineData(415, LogLevel.Warning)]
    public void Resolve_WhenTadssReturnsHttpError_LogsErrorOnlyFor400And409(int http, LogLevel expected)
    {
        foreach (var total in new[] { 200_000m, 3_000m })
        {
            var decision = DepositDecisionResolver.Resolve(Failure(TadssFailureKind.Http, http), total, Min);

            // đơn nhỏ vào a′ nhưng không được hạ mức log của lỗi nghiêm trọng hơn Warning
            Assert.Equal(expected, decision.LogLevel);
            Assert.Contains($"HTTP {http}", decision.LogMessage);
        }
    }

    [Fact]
    public void Resolve_WhenTadssFailsOnSmallOrder_MessageMentionsBothTheFailureAndTheCounterCollection()
    {
        var decision = DepositDecisionResolver.Resolve(Failure(TadssFailureKind.Timeout), 3_000m, Min);

        Assert.Contains("Timeout", decision.LogMessage);
        Assert.Contains("thu toàn bộ tại quầy", decision.LogMessage);
    }

    // ───────────────────────── (3) NEED_APPROVAL và REJECTED không xét tiền ─────────────────────────

    [Theory]
    [MemberData(nameof(TotalsAroundMinimum))]
    [Trait("Catalog", "BE-IT-03")]
    public void Resolve_WhenNeedApproval_AlwaysHoldsForApprovalRegardlessOfAmount(decimal total)
    {
        // Z-06
        var decision = DepositDecisionResolver.Resolve(NeedApproval(), total, Min);

        Assert.Equal(DepositBranch.NeedApproval, decision.Branch);
        Assert.Null(decision.DepositSource);
        Assert.Null(decision.OnlineAmount);
        Assert.Equal(DecisionId, decision.TadssDecisionId);
        Assert.Equal("HIGH_VALUE_REVIEW", decision.ApprovalReasonCode);
        Assert.Equal(["Đơn giá trị lớn"], decision.ApprovalReasons);
        Assert.Equal(0m, decision.DepositAmount);
    }

    [Fact]
    [Trait("Catalog", "BE-UT-12")]
    public void Resolve_WhenNeedApproval_StoresApprovalDeadlineAsTheSameInstantInUtc()
    {
        var decision = DepositDecisionResolver.Resolve(NeedApproval(), 100_000m, Min);

        // 20:30 +07:00 = 13:30 UTC
        Assert.Equal(new DateTime(2026, 11, 20, 13, 30, 0, DateTimeKind.Utc), decision.ApprovalExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, decision.ApprovalExpiresAtUtc!.Value.Kind);
    }

    [Theory]
    [MemberData(nameof(TotalsAroundMinimum))]
    [Trait("Catalog", "BE-IT-17")]
    public void Resolve_WhenRejected_AlwaysRejectsRegardlessOfAmount(decimal total)
    {
        // Z-07
        var decision = DepositDecisionResolver.Resolve(Rejected(), total, Min);

        Assert.Equal(DepositBranch.Rejected, decision.Branch);
        Assert.Null(decision.DepositSource);
        Assert.Null(decision.OnlineAmount);
        Assert.Equal(DecisionId, decision.TadssDecisionId);
        Assert.Null(decision.ApprovalExpiresAtUtc);
        Assert.Empty(decision.ApprovalReasons);
    }

    [Fact]
    public void Resolve_WhenRejectedOrNeedApproval_NeverProducesAConfirmedOrPaidBranchForAnyAmount()
    {
        // Z-09: không tổ hợp (trạng thái cần người xem × số tiền) nào được thành nhánh xác nhận hoặc thu tiền
        var totals = new[] { 0m, 1m, 4_999m, 5_000m, 2_000_000m, 100_000_000m };
        foreach (var result in new[] { NeedApproval(), Rejected() })
        foreach (var total in totals)
        {
            var branch = DepositDecisionResolver.Resolve(result, total, Min).Branch;
            Assert.True(branch is DepositBranch.NeedApproval or DepositBranch.Rejected, $"{branch} với tổng {total}");
        }
    }

    [Fact]
    public void Resolve_NeedApprovalBranch_CopiesTheReasonsSoLaterChangesToTheDtoDoNotLeak()
    {
        var dto = Result(TadssDecisionStatus.NEED_APPROVAL, approvalExpiresAt: DateTimeOffset.Parse("2026-11-20T20:30:00+07:00"), reasons: ["a"]);
        var decision = DepositDecisionResolver.Resolve(Ok(dto), 100_000m, Min);

        dto.Reasons.Add("b");

        Assert.Equal(["a"], decision.ApprovalReasons);
    }

    // ───────────────────────── Phòng thủ và đầu vào sai ─────────────────────────

    [Fact]
    public void Resolve_WhenApprovedButDepositMissing_FallsBackWithErrorLogAndNoDecisionId()
    {
        var decision = DepositDecisionResolver.Resolve(Ok(Result(TadssDecisionStatus.APPROVED)), 200_000m, Min);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Equal(DepositSource.FULL_PAYMENT_FALLBACK, decision.DepositSource);
        Assert.Null(decision.TadssDecisionId);
        Assert.Equal(LogLevel.Error, decision.LogLevel);
    }

    [Fact]
    public void Resolve_WhenNeedApprovalButDeadlineMissing_FallsBackWithErrorLogInsteadOfHoldingWithoutDeadline()
    {
        var decision = DepositDecisionResolver.Resolve(Ok(Result(TadssDecisionStatus.NEED_APPROVAL)), 200_000m, Min);

        Assert.Equal(DepositBranch.FullPaymentFallback, decision.Branch);
        Assert.Null(decision.ApprovalExpiresAtUtc);
        Assert.Equal(LogLevel.Error, decision.LogLevel);
    }

    [Fact]
    public void Resolve_WhenTotalIsNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepositDecisionResolver.Resolve(null, -1m, Min));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5000)]
    public void Resolve_WhenMinimumIsNotPositive_Throws(int min)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DepositDecisionResolver.Resolve(null, 100m, min));
    }

    [Fact]
    public void Resolve_WhenStatusIsUnknown_Throws()
    {
        var unknown = Ok(Result((TadssDecisionStatus)99, 1_000m));

        Assert.Throws<ArgumentOutOfRangeException>(() => DepositDecisionResolver.Resolve(unknown, 100_000m, Min));
    }

    [Fact]
    public void Resolve_WhenCalledTwiceWithSameInput_ReturnsEqualDecisions()
    {
        var result = Approved(40_000m, 0.2m);

        var first = DepositDecisionResolver.Resolve(result, 200_000m, Min);
        var second = DepositDecisionResolver.Resolve(result, 200_000m, Min);

        Assert.Equal(first with { ApprovalReasons = [] }, second with { ApprovalReasons = [] });
    }
}
