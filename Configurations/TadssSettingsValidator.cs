using Microsoft.Extensions.Options;

namespace SmashCourt_BE.Configurations;

/// <summary>
/// Kiểm tra <see cref="TadssSettings"/> khi khởi động và mỗi lần cấu hình được nạp lại.
/// Các khóa kết nối (BaseUrl, ApiKey) chỉ bắt buộc khi cờ bật để môi trường tắt TADSS không cần khóa.
/// </summary>
public class TadssSettingsValidator : IValidateOptions<TadssSettings>
{
    public ValidateOptionsResult Validate(string? name, TadssSettings options)
    {
        var errors = new List<string>();

        if (options.TimeoutMs is < 100 or > 5000)
            errors.Add($"{TadssSettings.SectionName}:TimeoutMs phải trong khoảng 100…5000 (hiện {options.TimeoutMs}).");

        if (options.ApprovedPaymentWindowMinutes is < 1 or > 60)
            errors.Add($"{TadssSettings.SectionName}:ApprovedPaymentWindowMinutes phải trong khoảng 1…60 (hiện {options.ApprovedPaymentWindowMinutes}).");

        if (!IsIanaTimeZone(options.BranchTimeZone))
            errors.Add($"{TadssSettings.SectionName}:BranchTimeZone '{options.BranchTimeZone}' không phải mã múi giờ IANA tìm được trên máy (mã Windows như 'SE Asia Standard Time' bị TADSS từ chối).");

        if (options.EnableTadssDecision)
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
                errors.Add($"{TadssSettings.SectionName}:BaseUrl phải là URL http/https tuyệt đối khi EnableTadssDecision bật.");

            if (string.IsNullOrWhiteSpace(options.ApiKey))
                errors.Add($"{TadssSettings.SectionName}:ApiKey bắt buộc khi EnableTadssDecision bật (đặt trong User Secrets hoặc biến môi trường).");
            else if (!options.ApiKey.All(c => c is > ' ' and <= '~'))
                errors.Add($"{TadssSettings.SectionName}:ApiKey chỉ được gồm ký tự ASCII hiển thị, không có khoảng trắng hay xuống dòng (thường do chép thừa vào User Secrets).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    // Cùng quy tắc với TADSS (DecisionRequestMapping.ResolveIanaTimeZone): mã phải là IANA và tìm được trên máy
    private static bool IsIanaTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _);
}
