using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using SmashCourt_BE.Models.Enums;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Task 2 của 6A-2a: đăng nhập giả bằng JWT thật do chính host phát. Dùng endpoint khách hàng <c>GET /api/me/bookings</c>
/// (policy <c>CustomerOnly</c>) để kiểm xác thực, phân quyền và <c>UserStatusMiddleware</c>.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class HttpAuthenticationTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    private const string CustomerBookings = "/api/me/bookings";

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string SignedToken(string userId, string role, DateTime expires, string key = SmashCourtWebApplicationFactory.JwtKey,
        string issuer = SmashCourtWebApplicationFactory.JwtIssuer, string audience = SmashCourtWebApplicationFactory.JwtAudience)
    {
        var token = new JwtSecurityToken(
            issuer, audience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId), new Claim(ClaimTypes.Role, role)],
            notBefore: expires.AddHours(-2), expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static HttpRequestMessage Get(string path, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer != null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    [Fact]
    public async Task Customer_WithValidToken_PassesTheCustomerOnlyPolicy()
    {
        var client = await CreateClientForAsync(await SeedUserAsync(UserRole.CUSTOMER));

        var response = await client.GetAsync(CustomerBookings);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithoutToken_IsUnauthorized()
    {
        var response = await (await CreateAnonymousClientAsync()).GetAsync(CustomerBookings);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.STAFF)]
    [InlineData(UserRole.BRANCH_MANAGER)]
    [InlineData(UserRole.OWNER)]
    public async Task NonCustomerRoles_AreForbiddenOnCustomerEndpoints(UserRole role)
    {
        var client = await CreateClientForAsync(await SeedUserAsync(role));

        var response = await client.GetAsync(CustomerBookings);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Token_WhenExpired_IsUnauthorized()
    {
        var user = await SeedUserAsync();
        var client = await CreateAnonymousClientAsync();

        var response = await client.SendAsync(Get(CustomerBookings, SignedToken(user.Id.ToString(), "CUSTOMER", DateTime.UtcNow.AddMinutes(-5))));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("issuer")]
    [InlineData("audience")]
    public async Task Token_WhenSignedOrAddressedWrongly_IsUnauthorized(string flaw)
    {
        var user = await SeedUserAsync();
        var token = flaw switch
        {
            "wrong-key" => SignedToken(user.Id.ToString(), "CUSTOMER", DateTime.UtcNow.AddHours(1), key: "another-signing-key-0123456789-abcdefghijklmnopqrstuvwxyz-0123456789"),
            "issuer" => SignedToken(user.Id.ToString(), "CUSTOMER", DateTime.UtcNow.AddHours(1), issuer: "someone-else"),
            _ => SignedToken(user.Id.ToString(), "CUSTOMER", DateTime.UtcNow.AddHours(1), audience: "someone-else")
        };

        var response = await (await CreateAnonymousClientAsync()).SendAsync(Get(CustomerBookings, token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public async Task Token_WhenMalformed_IsUnauthorized(string token)
    {
        var request = Get(CustomerBookings);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        var response = await (await CreateAnonymousClientAsync()).SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_WhenUserIsLocked_IsRejectedByUserStatusMiddlewareWithAccountLockedCode()
    {
        var client = await CreateClientForAsync(await SeedUserAsync(status: UserStatus.LOCKED));

        var response = await client.GetAsync(CustomerBookings);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Token_WhenUserDoesNotExist_IsRejectedWithUserNotFoundCode()
    {
        var token = SignedToken(Guid.NewGuid().ToString(), "CUSTOMER", DateTime.UtcNow.AddHours(1));

        var response = await (await CreateAnonymousClientAsync()).SendAsync(Get(CustomerBookings, token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("USER_NOT_FOUND", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task AccessTokenInQueryString_IsIgnoredOutsideTheHubs()
    {
        // B4: query access_token chỉ dành cho /hubs (SignalR); endpoint thường chỉ đọc header Authorization
        var user = await SeedUserAsync();
        var token = await TokenForAsync(user);

        var response = await (await CreateAnonymousClientAsync()).GetAsync($"{CustomerBookings}?access_token={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_IssuedBeforeUserWasLocked_StopsWorkingImmediately()
    {
        var user = await SeedUserAsync();
        var client = await CreateClientForAsync(user);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(CustomerBookings)).StatusCode);

        await WithDbAsync(async db =>
        {
            var tracked = await db.Users.FindAsync(user.Id);
            tracked!.Status = UserStatus.LOCKED;
            await db.SaveChangesAsync();
            return 0;
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(CustomerBookings)).StatusCode);
    }
}
