using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Tests.TestData;

namespace SmashCourt_BE.Tests.Unit.Extensions;

/// <summary>Khóa phân vùng rate limit (B1/D9): mỗi người một bộ đếm; chưa đăng nhập đếm theo IP; không có nguồn thì dùng một khóa chung.</summary>
[Trait("Category", TestCategories.Security)]
public class RateLimitPartitionKeyTests
{
    private static HttpContext Context(string? userId = null, string? ip = "203.0.113.7", bool authenticated = true, string claimType = ClaimTypes.NameIdentifier)
    {
        var context = new DefaultHttpContext();
        if (userId != null)
        {
            var identity = authenticated
                ? new ClaimsIdentity([new Claim(claimType, userId)], "test")
                : new ClaimsIdentity([new Claim(claimType, userId)]);
            context.User = new ClaimsPrincipal(identity);
        }
        context.Connection.RemoteIpAddress = ip == null ? null : IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public void Resolve_WhenAuthenticated_UsesTheUserId()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Equal($"user:{id}", RateLimitPartitionKey.Resolve(Context(id)));
    }

    [Fact]
    public void Resolve_ForTwoDifferentUsers_ReturnsTwoKeys()
    {
        Assert.NotEqual(
            RateLimitPartitionKey.Resolve(Context(Guid.NewGuid().ToString())),
            RateLimitPartitionKey.Resolve(Context(Guid.NewGuid().ToString())));
    }

    [Fact]
    public void Resolve_ForTheSameUserFromTwoAddresses_ReturnsTheSameKey()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Equal(
            RateLimitPartitionKey.Resolve(Context(id, "203.0.113.7")),
            RateLimitPartitionKey.Resolve(Context(id, "198.51.100.9")));
    }

    [Fact]
    public void Resolve_WhenAnonymous_UsesTheIpAddress()
    {
        Assert.Equal("ip:203.0.113.7", RateLimitPartitionKey.Resolve(Context()));
    }

    [Fact]
    public void Resolve_ForTwoAddresses_ReturnsTwoKeys()
    {
        Assert.NotEqual(
            RateLimitPartitionKey.Resolve(Context(ip: "203.0.113.7")),
            RateLimitPartitionKey.Resolve(Context(ip: "203.0.113.8")));
    }

    [Fact]
    public void Resolve_WhenThereIsNoAddress_UsesTheSharedUnknownKey()
    {
        Assert.Equal("unknown", RateLimitPartitionKey.Resolve(Context(ip: null)));
    }

    [Fact]
    public void Resolve_WhenAuthenticatedButNoIdClaim_FallsBackToTheIp()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "CUSTOMER")], "test")) };
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Equal("ip:203.0.113.7", RateLimitPartitionKey.Resolve(context));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WhenIdClaimIsBlank_FallsBackToTheIp(string blank)
    {
        Assert.Equal("ip:203.0.113.7", RateLimitPartitionKey.Resolve(Context(blank)));
    }

    [Fact]
    public void Resolve_WhenIdentityIsNotAuthenticated_IgnoresTheClaimAndUsesTheIp()
    {
        // Claim do client tự dựng mà chưa qua xác thực không được dùng làm khóa (tránh chọn khóa tùy ý để né giới hạn)
        Assert.Equal("ip:203.0.113.7", RateLimitPartitionKey.Resolve(Context(Guid.NewGuid().ToString(), authenticated: false)));
    }

    [Fact]
    public void Resolve_WhenOnlySubClaimIsPresent_UsesIt()
    {
        // Token của BE mang `sub`; tùy cấu hình ánh xạ claim, id có thể nằm ở `sub` thay vì NameIdentifier
        var id = Guid.NewGuid().ToString();

        Assert.Equal($"user:{id}", RateLimitPartitionKey.Resolve(Context(id, claimType: "sub")));
    }

    [Fact]
    public void Resolve_PrefersNameIdentifierOverSub()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "from-sub"), new Claim(ClaimTypes.NameIdentifier, "from-nameid")], "test"))
        };

        Assert.Equal("user:from-nameid", RateLimitPartitionKey.Resolve(context));
    }

    [Fact]
    public void Resolve_ForAnIPv4AddressMappedToIPv6_EqualsThePlainIPv4Key()
    {
        // Kestrel dual-stack báo cùng một máy khách dưới hai dạng
        Assert.Equal(
            RateLimitPartitionKey.Resolve(Context(ip: "203.0.113.7")),
            RateLimitPartitionKey.Resolve(Context(ip: "::ffff:203.0.113.7")));
    }

    [Fact]
    public void Resolve_ForIPv6_UsesTheFullAddress()
    {
        Assert.Equal("ip:2001:db8::1", RateLimitPartitionKey.Resolve(Context(ip: "2001:db8::1")));
    }

    [Fact]
    public void Resolve_WhenIdIsVeryLong_StillReturnsAKey()
    {
        var longId = new string('a', 5000);

        Assert.Equal($"user:{longId}", RateLimitPartitionKey.Resolve(Context(longId)));
    }
}
