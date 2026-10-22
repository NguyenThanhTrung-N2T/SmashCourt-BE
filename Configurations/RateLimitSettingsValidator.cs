using Microsoft.Extensions.Options;

namespace SmashCourt_BE.Configurations;

/// <summary>
/// Kiểm tra <see cref="RateLimitSettings"/> khi khởi động và mỗi lần cấu hình được nạp lại: số lượt và độ dài cửa sổ phải ≥ 1.
/// </summary>
public class RateLimitSettingsValidator : IValidateOptions<RateLimitSettings>
{
    public ValidateOptionsResult Validate(string? name, RateLimitSettings options)
    {
        var errors = new List<string>();

        Check(errors, nameof(RateLimitSettings.Login), options.Login);
        Check(errors, nameof(RateLimitSettings.Booking), options.Booking);
        Check(errors, nameof(RateLimitSettings.Sensitive), options.Sensitive);
        Check(errors, nameof(RateLimitSettings.AiPublic), options.AiPublic);
        Check(errors, nameof(RateLimitSettings.AiUser), options.AiUser);
        Check(errors, nameof(RateLimitSettings.AiManagement), options.AiManagement);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void Check(List<string> errors, string policy, RateLimitPolicySettings? settings)
    {
        var section = RateLimitSettings.SectionName;
        if (settings == null)
        {
            errors.Add($"{section}:{policy} không được để trống.");
            return;
        }

        if (settings.PermitLimit < 1)
            errors.Add($"{section}:{policy}:PermitLimit phải ≥ 1 (hiện {settings.PermitLimit}).");
        if (settings.WindowSeconds < 1)
            errors.Add($"{section}:{policy}:WindowSeconds phải ≥ 1 (hiện {settings.WindowSeconds}).");
    }
}
