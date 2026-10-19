namespace SmashCourt_BE.Tests.Helpers;

/// <summary>
/// Trait attribute for categorizing tests by functional area.
/// Usage: [TestCategory("Security")]
/// Run specific category: dotnet test --filter Category=Security
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class TestCategoryAttribute : Attribute
{
    public TestCategoryAttribute(string category)
    {
        Category = category;
    }

    public string Category { get; }

}

/// <summary>
/// Common test categories for the SmashCourt application.
/// </summary>
public static class TestCategories
{
    public const string Security = "Security";
    public const string Auth = "Auth";
    public const string Booking = "Booking";
    public const string Pricing = "Pricing";
    public const string Promotion = "Promotion";
    public const string Payment = "Payment";
    public const string Authorization = "Authorization";
    public const string Loyalty = "Loyalty";
    public const string Validation = "Validation";
    public const string AccessControl = "AccessControl";
    public const string Helper = "Helper";
    public const string Email = "Email";
    public const string Token = "Token";
    public const string Otp = "Otp";
}
