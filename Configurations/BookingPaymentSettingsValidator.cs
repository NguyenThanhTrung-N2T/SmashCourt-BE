using Microsoft.Extensions.Options;

namespace SmashCourt_BE.Configurations;

/// <summary>
/// Kiểm tra <see cref="BookingPaymentSettings"/> khi khởi động và mỗi lần cấu hình được nạp lại.
/// </summary>
public class BookingPaymentSettingsValidator : IValidateOptions<BookingPaymentSettings>
{
    public ValidateOptionsResult Validate(string? name, BookingPaymentSettings options)
    {
        var errors = new List<string>();
        const string section = BookingPaymentSettings.SectionName;

        if (options.InitialHoldMinutes < 1)
            errors.Add($"{section}:InitialHoldMinutes phải ≥ 1 (hiện {options.InitialHoldMinutes}).");

        if (options.RetryExtendMinutes < 1)
            errors.Add($"{section}:RetryExtendMinutes phải ≥ 1 (hiện {options.RetryExtendMinutes}).");

        if (options.PaymentFailureGraceMinutes < 1)
            errors.Add($"{section}:PaymentFailureGraceMinutes phải ≥ 1 (hiện {options.PaymentFailureGraceMinutes}).");

        if (options.MinOnlinePaymentAmount < 1)
            errors.Add($"{section}:MinOnlinePaymentAmount phải ≥ 1 (hiện {options.MinOnlinePaymentAmount}).");

        if (options.PaymentHoldMaxMinutes < options.InitialHoldMinutes)
            errors.Add($"{section}:PaymentHoldMaxMinutes ({options.PaymentHoldMaxMinutes}) phải ≥ InitialHoldMinutes ({options.InitialHoldMinutes}).");

        if (options.PaymentFailureGraceMinutes > options.PaymentHoldMaxMinutes)
            errors.Add($"{section}:PaymentFailureGraceMinutes ({options.PaymentFailureGraceMinutes}) phải ≤ PaymentHoldMaxMinutes ({options.PaymentHoldMaxMinutes}).");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
