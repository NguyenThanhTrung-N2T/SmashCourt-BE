using System.ComponentModel.DataAnnotations;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.DTOs;

/// <summary>
/// BE-UT-03: body duyệt / từ chối được kiểm trước khi gọi TADSS, theo đúng luật override của TADSS
/// (tỷ lệ trong [0, 1], tối đa 4 chữ số thập phân, không làm tròn; lý do 5..500 Unicode code point, không chỉ khoảng trắng).
/// </summary>
[TestCategory(TestCategories.Validation)]
[Trait("Catalog", "BE-UT-03")]
public class ApprovalRequestDtoTests
{
    private const string ValidReason = "Khách quen của chi nhánh";
    private const string Emoji = "😀"; // ngoài BMP: 2 đơn vị UTF-16, 1 code point

    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    private static IEnumerable<string> FieldsWithErrors(object dto) =>
        Validate(dto).SelectMany(r => r.MemberNames).Distinct();

    // ── Tỷ lệ cọc ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("0.3")]
    [InlineData("0.125")]
    [InlineData("0.0001")]
    [InlineData("0.9999")]
    [InlineData("0.30000")] // số 0 thừa sau JSON vẫn là 4 chữ số có nghĩa
    [InlineData("1.0000")]
    public void DepositRate_InRangeWithAtMostFourDecimals_IsValid(string rate)
    {
        var dto = new ApproveBookingDto { DepositRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), Reason = ValidReason };

        Assert.Empty(Validate(dto));
    }

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("1.0001")]
    [InlineData("0.12345")] // không làm tròn âm thầm thành 0.1235
    [InlineData("0.00001")]
    [InlineData("2")]
    public void DepositRate_OutOfRangeOrTooPrecise_IsRejectedOnDepositRate(string rate)
    {
        var dto = new ApproveBookingDto { DepositRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), Reason = ValidReason };

        var result = Assert.Single(Validate(dto));
        Assert.Equal([nameof(ApproveBookingDto.DepositRate)], result.MemberNames);
        Assert.Equal("Tỷ lệ cọc phải từ 0 đến 1 và có tối đa 4 chữ số thập phân", result.ErrorMessage);
    }

    [Fact]
    public void DepositRate_Missing_IsRejectedOnDepositRate()
    {
        var dto = new ApproveBookingDto { DepositRate = null, Reason = ValidReason };

        Assert.Equal([nameof(ApproveBookingDto.DepositRate)], FieldsWithErrors(dto));
    }

    // ── Lý do (dùng chung cho duyệt và từ chối) ────────────────────────────

    public static TheoryData<string> ValidReasons => new()
    {
        new string('a', 5),
        new string('a', 500),
        string.Concat(Enumerable.Repeat(Emoji, 5)),   // 10 đơn vị UTF-16 nhưng 5 code point
        string.Concat(Enumerable.Repeat(Emoji, 500)), // 1.000 đơn vị UTF-16 nhưng 500 code point
        "  Khách quen  ",
        "Đã gọi xác nhận"
    };

    public static TheoryData<string?> InvalidReasons => new()
    {
        null,
        "",
        "    ",
        "\t\n  ",
        new string('a', 4),
        new string('a', 501),
        string.Concat(Enumerable.Repeat(Emoji, 4)),   // 8 đơn vị UTF-16 nhưng chỉ 4 code point
        string.Concat(Enumerable.Repeat(Emoji, 501))
    };

    [Theory]
    [MemberData(nameof(ValidReasons))]
    public void Reason_Valid_PassesForApproveAndReject(string reason)
    {
        Assert.Empty(Validate(new ApproveBookingDto { DepositRate = 0.3m, Reason = reason }));
        Assert.Empty(Validate(new RejectBookingDto { Reason = reason }));
    }

    [Theory]
    [MemberData(nameof(InvalidReasons))]
    public void Reason_Invalid_IsRejectedOnReasonForApproveAndReject(string? reason)
    {
        Assert.Equal([nameof(ApproveBookingDto.Reason)], FieldsWithErrors(new ApproveBookingDto { DepositRate = 0.3m, Reason = reason }));
        Assert.Equal([nameof(RejectBookingDto.Reason)], FieldsWithErrors(new RejectBookingDto { Reason = reason }));
    }

    [Fact]
    public void BothFieldsInvalid_ReportsBoth()
    {
        var dto = new ApproveBookingDto { DepositRate = 1.5m, Reason = "abc" };

        Assert.Equal(
            new[] { nameof(ApproveBookingDto.DepositRate), nameof(ApproveBookingDto.Reason) },
            FieldsWithErrors(dto).OrderBy(x => x));
    }

    // ── Đếm code point ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 3)]
    [InlineData("Đặt sân", 7)]
    [InlineData("😀", 1)]
    [InlineData("a😀b", 3)]
    public void CountCodePoints_CountsRunesNotUtf16Units(string text, int expected)
    {
        Assert.Equal(expected, StringHelper.CountCodePoints(text));
    }
}
