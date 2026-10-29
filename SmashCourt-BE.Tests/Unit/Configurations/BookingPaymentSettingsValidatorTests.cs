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
    public void Defaults_PaymentLinkBufferIsTwoMinutes()
    {
        // P6-27 (chốt 29/10): link hết hạn trước hạn thanh toán của đơn 2 phút
        Assert.Equal(2, new BookingPaymentSettings().PaymentLinkBufferMinutes);
    }

    [Fact]
    public void Validate_WhenPaymentLinkBufferNegative_Fails()
    {
        var result = _validator.Validate(null, new BookingPaymentSettings { PaymentLinkBufferMinutes = -1 });

        Assert.True(result.Failed);
        Assert.Contains(nameof(BookingPaymentSettings.PaymentLinkBufferMinutes), result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenPaymentLinkBufferZero_IsValid()
    {
        var settings = new BookingPaymentSettings { PaymentLinkBufferMinutes = 0, InitialHoldMinutes = 1, RetryExtendMinutes = 1 };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(2, 2)]  // đơn mới chỉ còn đúng khoảng đệm ⇒ link sống 0
    [InlineData(2, 1)]
    [InlineData(5, 4)]
    public void Validate_WhenInitialHoldNotAboveBufferPlusOneMinute_Fails(int buffer, int initialHold)
    {
        var settings = new BookingPaymentSettings { PaymentLinkBufferMinutes = buffer, InitialHoldMinutes = initialHold };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(nameof(BookingPaymentSettings.InitialHoldMinutes), result.FailureMessage);
        Assert.Contains(nameof(BookingPaymentSettings.PaymentLinkBufferMinutes), result.FailureMessage);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    public void Validate_WhenRetryExtendNotAboveBufferPlusOneMinute_Fails(int buffer, int retryExtend)
    {
        var settings = new BookingPaymentSettings
        {
            PaymentLinkBufferMinutes = buffer,
            InitialHoldMinutes = 10,
            RetryExtendMinutes = retryExtend
        };

        var result = _validator.Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(nameof(BookingPaymentSettings.RetryExtendMinutes), result.FailureMessage);
    }

    [Fact]
    public void Validate_WhenHoldsExactlyBufferPlusOneMinute_IsValid()
    {
        // Biên: đơn mới và lần thanh toán lại đầu vẫn tạo được link sống đúng 1 phút
        var settings = new BookingPaymentSettings { PaymentLinkBufferMinutes = 2, InitialHoldMinutes = 3, RetryExtendMinutes = 3 };

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
