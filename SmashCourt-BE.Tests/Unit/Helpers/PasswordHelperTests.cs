using SmashCourt_BE.Helpers;

namespace SmashCourt_BE.Tests.Unit.Helpers;

public class PasswordHelperTests
{
    // Hash bcrypt hệ số 12 của "Perf@12345" (hằng không bí mật của dữ liệu perf): mật khẩu băm trước khi hạ hệ số vẫn phải đăng nhập được
    private const string CostTwelveHash = "$2b$12$ojCdr3JFXIzOM7tYET2c4.yauitJ945GuGxSEtFlcJX4kMhHQJS36";

    [Fact]
    public void HashPassword_ProducesAHashThatVerifiesOnlyTheSamePassword()
    {
        var hash = PasswordHelper.HashPassword("Mat-khau@2026");

        Assert.True(BCrypt.Net.BCrypt.Verify("Mat-khau@2026", hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("mat-khau@2026", hash));
    }

    [Fact]
    public void HashPassword_SaltsEveryHash()
    {
        Assert.NotEqual(PasswordHelper.HashPassword("Mat-khau@2026"), PasswordHelper.HashPassword("Mat-khau@2026"));
    }

    [Fact]
    public void Verify_StillAcceptsHashesCreatedWithTheOldWorkFactor()
    {
        Assert.True(BCrypt.Net.BCrypt.Verify("Perf@12345", CostTwelveHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("Perf@12346", CostTwelveHash));
    }
}
