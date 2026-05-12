using System.Text.RegularExpressions;

namespace SmashCourt_BE.Helpers;

/// <summary>
/// Helper để parse User-Agent string thành device name dễ đọc
/// </summary>
public static class UserAgentParser
{
    /// <summary>
    /// Parse User-Agent string thành device name dễ đọc
    /// Ví dụ: "Chrome on Windows", "Safari on iPhone", "Firefox on macOS"
    /// </summary>
    public static string ParseToDeviceName(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "Unknown Device";

        // Truncate nếu quá dài
        if (userAgent.Length > 500)
            userAgent = userAgent.Substring(0, 500);

        var browser = DetectBrowser(userAgent);
        var os = DetectOS(userAgent);

        return $"{browser} on {os}";
    }

    /// <summary>
    /// Detect browser từ User-Agent
    /// </summary>
    private static string DetectBrowser(string userAgent)
    {
        // Order matters - check specific browsers first
        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
            return "Edge";
        
        if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) && 
            !userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
            return "Chrome";
        
        if (userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase) && 
            !userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
            return "Safari";
        
        if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
            return "Firefox";
        
        if (userAgent.Contains("Opera/", StringComparison.OrdinalIgnoreCase) || 
            userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase))
            return "Opera";
        
        if (userAgent.Contains("MSIE", StringComparison.OrdinalIgnoreCase) || 
            userAgent.Contains("Trident/", StringComparison.OrdinalIgnoreCase))
            return "Internet Explorer";

        return "Unknown Browser";
    }

    /// <summary>
    /// Detect OS từ User-Agent
    /// </summary>
    private static string DetectOS(string userAgent)
    {
        // Mobile devices
        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            return "iPhone";
        
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            return "iPad";
        
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            // Try to extract device name
            var match = Regex.Match(userAgent, @"Android.*?;\s*([^)]+)\)", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                var deviceName = match.Groups[1].Value.Trim();
                // Clean up common patterns
                deviceName = deviceName.Replace("Build/", "").Trim();
                if (!string.IsNullOrWhiteSpace(deviceName) && deviceName.Length < 30)
                    return $"Android ({deviceName})";
            }
            return "Android";
        }

        // Desktop OS
        if (userAgent.Contains("Windows NT 10.0", StringComparison.OrdinalIgnoreCase))
            return "Windows 10/11";
        
        if (userAgent.Contains("Windows NT 6.3", StringComparison.OrdinalIgnoreCase))
            return "Windows 8.1";
        
        if (userAgent.Contains("Windows NT 6.2", StringComparison.OrdinalIgnoreCase))
            return "Windows 8";
        
        if (userAgent.Contains("Windows NT 6.1", StringComparison.OrdinalIgnoreCase))
            return "Windows 7";
        
        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            return "Windows";
        
        if (userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase) || 
            userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase))
            return "macOS";
        
        if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            return "Linux";
        
        if (userAgent.Contains("CrOS", StringComparison.OrdinalIgnoreCase))
            return "Chrome OS";

        return "Unknown OS";
    }

    /// <summary>
    /// Truncate User-Agent string để lưu vào database (max 500 chars)
    /// </summary>
    public static string? TruncateUserAgent(string? userAgent, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return null;

        return userAgent.Length > maxLength 
            ? userAgent.Substring(0, maxLength) 
            : userAgent;
    }
}
