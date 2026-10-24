using SmashCourt_BE.Common;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Common;

/// <summary>
/// Giá trị chuỗi của mã lỗi luồng cọc là hợp đồng với FE (DEPOSIT_APPROVAL_API Mục 5): FE rẽ nhánh theo đúng chuỗi này.
/// </summary>
[TestCategory(TestCategories.Validation)]
[Trait("Catalog", "BE-UT-04")]
public class DepositApprovalErrorCodesTests
{
    [Theory]
    [InlineData(ErrorCodes.BookingRejected, "BOOKING_REJECTED")]
    [InlineData(ErrorCodes.BookingNotPendingApproval, "BOOKING_NOT_PENDING_APPROVAL")]
    [InlineData(ErrorCodes.ApprovalAlreadyResolved, "APPROVAL_ALREADY_RESOLVED")]
    [InlineData(ErrorCodes.ApprovalExpired, "APPROVAL_EXPIRED")]
    [InlineData(ErrorCodes.ApprovalDecisionNotFound, "APPROVAL_DECISION_NOT_FOUND")]
    [InlineData(ErrorCodes.TadssUnavailable, "TADSS_UNAVAILABLE")]
    public void ErrorCode_MatchesApiContract(string actual, string expected) => Assert.Equal(expected, actual);

    [Fact]
    public void ErrorCodes_AreUnique()
    {
        var values = typeof(ErrorCodes).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }
}
