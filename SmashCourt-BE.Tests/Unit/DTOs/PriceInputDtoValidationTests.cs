using System.ComponentModel.DataAnnotations;
using System.Globalization;
using SmashCourt_BE.DTOs.Branch;
using SmashCourt_BE.DTOs.PriceConfig;
using SmashCourt_BE.DTOs.Service;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.DTOs;

/// <summary>
/// Giá sân và giá dịch vụ nhập vào phải là số đồng nguyên: VNPay và thu tiền mặt không nhận phần lẻ của đồng.
/// </summary>
[Trait("Category", TestCategories.Pricing)]
[Trait("Category", TestCategories.Validation)]
public class PriceInputDtoValidationTests
{
    public static TheoryData<string, bool> Prices => new()
    {
        { "85000", true },
        { "85000.00", true },
        { "85000.5", false },
        { "85000.01", false }
    };

    [Theory]
    [MemberData(nameof(Prices))]
    public void PriceSlotInput_WeekdayPrice_MustBeWholeDong(string price, bool valid)
    {
        var dto = new PriceSlotInput { StartTime = "06:00:00", EndTime = "12:00:00", WeekdayPrice = Parse(price), WeekendPrice = 90_000m };

        AssertPriceValidation(dto, nameof(PriceSlotInput.WeekdayPrice), valid);
    }

    [Theory]
    [MemberData(nameof(Prices))]
    public void PriceSlotInput_WeekendPrice_MustBeWholeDong(string price, bool valid)
    {
        var dto = new PriceSlotInput { StartTime = "06:00:00", EndTime = "12:00:00", WeekdayPrice = 80_000m, WeekendPrice = Parse(price) };

        AssertPriceValidation(dto, nameof(PriceSlotInput.WeekendPrice), valid);
    }

    [Fact]
    public void PriceSlotInput_WhenBothPricesAreFractional_ReportsBoth()
    {
        var dto = new PriceSlotInput { StartTime = "06:00:00", EndTime = "12:00:00", WeekdayPrice = 80_000.5m, WeekendPrice = 90_000.5m };

        var members = Validate(dto).SelectMany(r => r.MemberNames).ToList();

        Assert.Equal([nameof(PriceSlotInput.WeekdayPrice), nameof(PriceSlotInput.WeekendPrice)], members);
    }

    [Theory]
    [MemberData(nameof(Prices))]
    public void CreateServiceDto_DefaultPrice_MustBeWholeDong(string price, bool valid)
    {
        var dto = new CreateServiceDto { Name = "Nước suối", Unit = "chai", DefaultPrice = Parse(price) };

        AssertPriceValidation(dto, nameof(CreateServiceDto.DefaultPrice), valid);
    }

    [Theory]
    [MemberData(nameof(Prices))]
    public void UpdateServiceDto_DefaultPrice_MustBeWholeDong(string price, bool valid)
    {
        var dto = new UpdateServiceDto { Name = "Nước suối", Unit = "chai", DefaultPrice = Parse(price) };

        AssertPriceValidation(dto, nameof(UpdateServiceDto.DefaultPrice), valid);
    }

    [Theory]
    [MemberData(nameof(Prices))]
    public void AddServiceToBranchDto_Price_MustBeWholeDong(string price, bool valid)
    {
        var dto = new AddServiceToBranchDto { ServiceId = Guid.NewGuid(), Price = Parse(price) };

        AssertPriceValidation(dto, nameof(AddServiceToBranchDto.Price), valid);
    }

    [Fact]
    public void AddServiceToBranchDto_WithoutPrice_UsesTheSystemPriceAndIsValid()
    {
        Assert.Empty(Validate(new AddServiceToBranchDto { ServiceId = Guid.NewGuid() }));
    }

    [Theory]
    [MemberData(nameof(Prices))]
    public void UpdateBranchServiceDto_Price_MustBeWholeDong(string price, bool valid)
    {
        var dto = new UpdateBranchServiceDto { Price = Parse(price) };

        AssertPriceValidation(dto, nameof(UpdateBranchServiceDto.Price), valid);
    }

    private static decimal Parse(string price) => decimal.Parse(price, CultureInfo.InvariantCulture);

    private static void AssertPriceValidation(object dto, string member, bool valid)
    {
        var errors = Validate(dto);
        if (valid)
        {
            Assert.Empty(errors);
            return;
        }

        var error = Assert.Single(errors);
        Assert.Equal([member], error.MemberNames);
        Assert.Contains("số đồng nguyên", error.ErrorMessage);
    }

    // Chạy đủ như model binding: thuộc tính trước, IValidatableObject sau
    private static List<ValidationResult> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }
}
