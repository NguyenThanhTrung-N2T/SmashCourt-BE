using SmashCourt_BE.Helpers;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Helpers;

[Trait("Category", TestCategories.Pricing)]
[Trait("Category", TestCategories.Helper)]
public class MoneyHelperTests
{
    [Theory]
    [InlineData("33333.3333", 33_333)]
    [InlineData("66666.6667", 66_667)]
    [InlineData("42500.5", 42_501)]
    [InlineData("3038.5", 3_039)]
    [InlineData("2.5", 3)]
    [InlineData("0.4999", 0)]
    [InlineData("85000", 85_000)]
    [InlineData("0", 0)]
    public void RoundVnd_RoundsToWholeDong_HalfUp(string amount, int expected)
    {
        Assert.Equal(expected, MoneyHelper.RoundVnd(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void RoundVnd_ThirdOfAPrice_IsWholeDong()
    {
        var third = 100_000m * 1m / 3m;

        Assert.Equal(33_333m, MoneyHelper.RoundVnd(third));
    }

    [Theory]
    [InlineData("85000", true)]
    [InlineData("85000.00", true)]
    [InlineData("0", true)]
    [InlineData("85000.5", false)]
    [InlineData("85000.01", false)]
    [InlineData("0.1", false)]
    public void IsWholeVnd_OnlyAcceptsWholeDong(string amount, bool expected)
    {
        Assert.Equal(expected, MoneyHelper.IsWholeVnd(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
