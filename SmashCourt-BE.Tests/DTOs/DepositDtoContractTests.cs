using System.Reflection;
using SmashCourt_BE.DTOs.Booking;
using SmashCourt_BE.Tests.Helpers;

namespace SmashCourt_BE.Tests.DTOs;

/// <summary>BE-UT-07 (phần hình dạng DTO): trường mới có mặt, trường nội bộ của TADSS không bao giờ lộ ra FE.</summary>
[Trait("Category", TestCategories.Booking)]
[Trait("Catalog", "BE-UT-07")]
public class DepositDtoContractTests
{
    private static readonly string[] ForbiddenTadssFields = ["DecisionId", "TadssDecisionId", "StrategyName", "ExecutionType", "FallbackUsed"];

    private static IEnumerable<Type> ApiDtoTypes() =>
        typeof(BookingDto).Assembly.GetTypes()
            .Where(t => t.IsClass && t.Namespace != null && t.Namespace.StartsWith("SmashCourt_BE.DTOs", StringComparison.Ordinal));

    [Fact]
    public void NoApiDto_ExposesInternalTadssFields()
    {
        var leaks = ApiDtoTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => ForbiddenTadssFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.Empty(leaks);
        Assert.Contains(typeof(OnlineBookingResponse), ApiDtoTypes());
    }

    [Theory]
    [InlineData(typeof(BookingDto), new[]
    {
        "DepositRate", "DepositAmount", "RemainingAmount", "DepositSource", "ApprovalExpiresAt", "ApprovalReasonCode",
        "ApprovalReasons", "CancelReason", "ExpiresAt"
    })]
    [InlineData(typeof(OnlineBookingResponse), new[]
    {
        "BookingCode", "Status", "PaymentUrl", "ExpiresAt", "DepositRate", "DepositAmount", "RemainingAmount", "DepositSource",
        "ApprovalExpiresAt"
    })]
    public void DepositFields_AreNullable(Type dto, string[] properties)
    {
        // Mọi trường mới có thể null (2.2); trừ BookingCode/Status của response luôn có giá trị
        var nullability = new NullabilityInfoContext();
        foreach (var name in properties)
        {
            var property = dto.GetProperty(name);
            Assert.NotNull(property);
            var nullable = Nullable.GetUnderlyingType(property.PropertyType) != null
                           || nullability.Create(property).ReadState == NullabilityState.Nullable;
            Assert.Equal(name is not ("BookingCode" or "Status"), nullable);
        }
    }
}
