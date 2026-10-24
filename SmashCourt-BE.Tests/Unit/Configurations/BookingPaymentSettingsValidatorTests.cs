using SmashCourt_BE.Configurations;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Configurations;

[Trait("Category", TestCategories.Configuration)]
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "BE-UT-01")]
public class BookingPaymentSettingsValidatorTests
{
    private readonly BookingPaymentSettingsValidator _validator = new();

    [Fact]
    public void Validate_WhenDefaults_IsValid()
    {
        var result = _validator.Validate(null, new BookingPaymentSettings());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(nameof(BookingPaymentSettings.InitialHoldMinutes))]
    [InlineData(nameof(BookingPaymentSettings.RetryExtendMinutes))]
    [InlineData(nameof(BookingPaymentSettings.PaymentFailureGraceMinutes))]
    public void Validate_WhenMinuteValueIsZero_FailsMentioningTheKey(string key)
    {
        var settings = new BookingPaymentSettings();
        typeof(BookingPaymentSettings).GetProperty(key)!.SetValue(settings, 0);

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(key, result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenMinOnlinePaymentAmountBelowOne_Fails()
    {
        var result = _validator.Validate(null, new BookingPaymentSettings { MinOnlinePaymentAmount = 0m });

        Assert.True(result.Failed);
        Assert.Contains("MinOnlinePaymentAmount", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenHoldMaxBelowInitialHold_Fails()
    {
        var settings = new BookingPaymentSettings { InitialHoldMinutes = 20, PaymentHoldMaxMinutes = 15 };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("PaymentHoldMaxMinutes", result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenHoldMaxEqualsInitialHold_IsValid()
    {
        var settings = new BookingPaymentSettings { InitialHoldMinutes = 15, PaymentHoldMaxMinutes = 15 };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenGraceAboveHoldMax_Fails()
    {
        var settings = new BookingPaymentSettings { PaymentFailureGraceMinutes = 16, PaymentHoldMaxMinutes = 15 };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains("PaymentFailureGraceMinutes", result.FailureMessage);
    }
}
