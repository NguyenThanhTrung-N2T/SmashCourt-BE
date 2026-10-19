namespace SmashCourt_BE.Tests.TestData;

/// <summary>
/// Centralized constants for test data to avoid magic strings and numbers.
/// </summary>
internal static class TestConstants
{
    // Authentication & Security
    public const string TestEmail = "test@example.com";
    public const string TestEmailUpperCase = "TEST@EXAMPLE.COM";
    public const string TestEmailWithSpaces = "  test@example.com  ";
    public const string CorrectPassword = "Correct123!";
    public const string WrongPassword = "Wrong123!";
    public const string TestSecret = "test-secret-key-for-hmac-signing-at-least-32-bytes";
    public const string TestFullName = "Test User";
    
    // JWT Configuration
    public const string JwtKey = "test-key-that-is-long-enough-for-jwt-signing-minimum-32-bytes";
    public const string JwtIssuer = "test-issuer";
    public const string JwtAudience = "test-audience";
    public const int JwtAccessTokenExpirationMinutes = 15;
    
    // OTP
    public const string ValidOtpCode = "123456";
    public const string InvalidOtpCode = "654321";
    public const int OtpExpirationMinutes = 5;
    public const int OtpMaxAttempts = 5;
    
    // Booking & Pricing
    public const decimal StandardBookingAmount = 1_000_000m;
    public const decimal MinimumBookingAmount = 100_000m;
    public const decimal MaximumBookingAmount = 10_000_000m;
    public const decimal StandardCourtPrice = 200_000m;
    public const decimal PeakHourPrice = 300_000m;
    public const decimal OffPeakPrice = 150_000m;
    
    // Promotion & Discount
    public const decimal StandardDiscountPercent = 10m;
    public const decimal MaxDiscountPercent = 50m;
    public const decimal StandardFixedDiscount = 50_000m;
    public const decimal MaxDiscountAmount = 500_000m;
    
    // Time Slots
    public const int StandardSlotDurationMinutes = 90;
    public const int MinimumSlotDurationMinutes = 60;
    public static readonly TimeSpan MorningStartTime = new(9, 0, 0);
    public static readonly TimeSpan MorningEndTime = new(10, 30, 0);
    public static readonly TimeSpan EveningStartTime = new(18, 0, 0);
    public static readonly TimeSpan EveningEndTime = new(19, 30, 0);
    public static readonly TimeSpan PeakHourStart = new(17, 0, 0);
    public static readonly TimeSpan PeakHourEnd = new(21, 0, 0);
    
    // Account Locking
    public const int MaxFailedLoginAttempts = 5;
    public const int AccountLockDurationMinutes = 15;
    
    // Dates
    public static readonly DateOnly StandardTestDate = new(2026, 6, 15);
    public static readonly DateOnly WeekendDate = new(2026, 6, 14); // Saturday
    public static readonly DateOnly WeekdayDate = new(2026, 6, 16); // Monday
    public static readonly DateTime StandardDateTime = new(2026, 6, 15, 14, 30, 0);
    
    // Check-in Window
    public const int CheckInWindowMinutesBefore = 15;
    public const int CheckInWindowMinutesAfter = 15;
    
    // Sports
    public const string DefaultSport = "BADMINTON";
    public const string TennisSport = "TENNIS";
    public const string PickleballSport = "PICKLEBALL";
    
    // Pagination
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;
    
    // Promotion
    public const string TestPromotionCode = "TEST2026";
    public const int DefaultPromotionUsageLimit = 100;
    public const int DefaultUserPromotionUsageLimit = 1;
}
