using System.Net;
using System.Text;
using System.Text.Json;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Task 3 của 6A-2a (B1/D9): rate limit đếm theo từng người (user hoặc IP) thay vì một bộ đếm chung toàn hệ thống, `UseRateLimiter` chạy
/// sau xác thực, giới hạn đọc từ cấu hình. Host test để giới hạn rất cao; mỗi test hạ giới hạn của đúng policy mình dùng bằng
/// <c>SetSetting</c> rồi trả về mặc định. Giới hạn áp cho bộ đếm tạo sau đó, nên mỗi test dùng user/IP mới.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
[Trait("Category", "Security")]
public sealed class HttpRateLimitTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    private const string BookingPath = "/api/bookings/online";
    private const string LoginPath = "/api/auth/login";
    private const string UploadPath = "/api/uploads/image";

    private static int _ipCounter;

    private static string NewIp()
    {
        var n = Interlocked.Increment(ref _ipCounter);
        return $"198.51.{n / 250 % 250 + 1}.{n % 250 + 1}";
    }

    private async Task<IDisposable> LimitAsync(string policyProperty, int permit, int windowSeconds = 60)
    {
        var factory = await GetFactoryAsync();
        var permitKey = $"RateLimiting:{policyProperty}:PermitLimit";
        var windowKey = $"RateLimiting:{policyProperty}:WindowSeconds";
        factory.SetSetting(permitKey, permit.ToString());
        factory.SetSetting(windowKey, windowSeconds.ToString());
        return new Restore(() =>
        {
            factory.SetSetting(permitKey, null);
            factory.SetSetting(windowKey, null);
        });
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    // Endpoint upload chỉ nhận multipart: gửi JSON bị bộ chọn endpoint trả 415 trước cả middleware rate limit
    private static MultipartFormDataContent Form() => new() { { new StringContent("x"), "folder" } };

    private static StringContent Json(string body = "{}") => new(body, Encoding.UTF8, "application/json");

    private static Task<HttpResponseMessage> Book(HttpClient client) => client.PostAsync(BookingPath, Json());

    private static async Task<int> CountAllowedUntilLimitAsync(Func<Task<HttpResponseMessage>> call, int max)
    {
        var allowed = 0;
        for (var i = 0; i < max; i++)
        {
            using var response = await call();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                break;
            allowed++;
        }
        return allowed;
    }

    // ───────────────────────── Đếm theo từng người ─────────────────────────

    [Fact]
    public async Task SameUser_AfterTheLimit_Gets429WithTheOriginalEnvelope()
    {
        using var _ = await LimitAsync("Booking", 3);
        var client = await CreateClientForAsync(await SeedUserAsync(), NewIp());

        for (var i = 0; i < 3; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Book(client)).StatusCode);
        // Vượt giới hạn phải bị từ chối ngay (QueueLimit = 0), không xếp hàng chờ hết cửa sổ
        var blocked = await Book(client).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.StartsWith("application/json", blocked.Content.Headers.ContentType!.MediaType + "");
        using var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("OTP_LIMIT_EXCEEDED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Bạn gửi quá nhiều request, vui lòng thử lại sau", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DifferentUsers_HaveIndependentBuckets_ThisWasTheProductionBug()
    {
        // Trước đây một bộ đếm chung: người thứ N+1 trong phút bị chặn dù chưa đặt lần nào
        using var _ = await LimitAsync("Booking", 2);
        var userA = await CreateClientForAsync(await SeedUserAsync(), NewIp());
        var userB = await CreateClientForAsync(await SeedUserAsync(), NewIp());

        Assert.Equal(2, await CountAllowedUntilLimitAsync(() => Book(userA), 10));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Book(userB)).StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Book(userB)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Book(userB)).StatusCode);
    }

    [Fact]
    public async Task SameUser_FromTwoAddresses_SharesOneBucket()
    {
        // Đã đăng nhập thì đếm theo người, không theo IP: đổi IP không né được giới hạn
        using var _ = await LimitAsync("Booking", 2);
        var user = await SeedUserAsync();
        var fromHome = await CreateClientForAsync(user, NewIp());
        var fromCafe = await CreateClientForAsync(user, NewIp());

        await Book(fromHome);
        await Book(fromCafe);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Book(fromHome)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Book(fromCafe)).StatusCode);
    }

    [Fact]
    public async Task UsersBehindTheSameAddress_DoNotShareABucket_AndNeitherDoesAnonymousTraffic()
    {
        // Chứng minh thứ tự middleware: nếu UseRateLimiter còn chạy trước xác thực thì mọi request chỉ có IP và ba bên dùng chung một bộ đếm
        using var _ = await LimitAsync("Booking", 2);
        var ip = NewIp();
        var userA = await CreateClientForAsync(await SeedUserAsync(), ip);
        var userB = await CreateClientForAsync(await SeedUserAsync(), ip);
        var anonymous = await CreateAnonymousClientAsync(ip);

        Assert.Equal(2, await CountAllowedUntilLimitAsync(() => Book(userA), 10));

        Assert.Equal(2, await CountAllowedUntilLimitAsync(() => Book(userB), 10));
        Assert.Equal(2, await CountAllowedUntilLimitAsync(() => Book(anonymous), 10));
    }

    [Fact]
    public async Task Anonymous_IsCountedPerIpAddress()
    {
        using var _ = await LimitAsync("Booking", 2);
        var ipX = await CreateAnonymousClientAsync(NewIp());
        var ipY = await CreateAnonymousClientAsync(NewIp());

        Assert.Equal(2, await CountAllowedUntilLimitAsync(() => Book(ipX), 10));

        Assert.Equal(2, await CountAllowedUntilLimitAsync(() => Book(ipY), 10));
    }

    [Fact]
    public async Task Anonymous_WithoutAnyAddress_SharesOneUnknownBucket()
    {
        // Không xác định được nguồn thì dùng một khóa chung (cố ý chặt, không bỏ qua giới hạn). Dùng policy `sensitive` riêng cho test này
        using var _ = await LimitAsync("Sensitive", 2, windowSeconds: 2);
        var first = await CreateAnonymousClientAsync();
        var second = await CreateAnonymousClientAsync();

        await first.PostAsync(UploadPath, Form());
        await second.PostAsync(UploadPath, Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAsync(UploadPath, Form())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await second.PostAsync(UploadPath, Form())).StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(2.3)); // trả bộ đếm chung về trống cho test khác
    }

    // ───────────────────────── Đăng nhập (lỗi nặng nhất của bộ đếm chung) ─────────────────────────

    [Fact]
    public async Task Login_IsLimitedPerAddress_SoOneAttackerCannotLockEveryoneOut()
    {
        using var _ = await LimitAsync("Login", 3);
        var attacker = await CreateAnonymousClientAsync(NewIp());
        var customer = await CreateAnonymousClientAsync(NewIp());
        const string wrongLogin = """{"email":"nobody@example.com","password":"wrong-password"}""";

        var attackerAllowed = await CountAllowedUntilLimitAsync(() => attacker.PostAsync(LoginPath, Json(wrongLogin)), 10);

        Assert.Equal(3, attackerAllowed);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await customer.PostAsync(LoginPath, Json(wrongLogin))).StatusCode);
    }

    // ───────────────────────── Cấu hình và cách ly ─────────────────────────

    [Fact]
    public async Task Limit_ResetsWhenTheWindowPasses()
    {
        using var _ = await LimitAsync("Booking", 2, windowSeconds: 1);
        var client = await CreateClientForAsync(await SeedUserAsync(), NewIp());
        Assert.Equal(2, await CountAllowedUntilLimitAsync(() => Book(client), 10));

        await Task.Delay(TimeSpan.FromSeconds(1.3));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await Book(client)).StatusCode);
    }

    [Fact]
    public async Task Endpoint_WithoutAPolicy_IsNeverLimited()
    {
        using var _ = await LimitAsync("Booking", 1);
        var client = await CreateClientForAsync(await SeedUserAsync(), NewIp());
        await Book(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Book(client)).StatusCode);

        for (var i = 0; i < 8; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/branches/basic")).StatusCode);
    }

    [Fact]
    public async Task Policies_AreCountedSeparatelyForTheSameUser()
    {
        using var booking = await LimitAsync("Booking", 1);
        using var sensitive = await LimitAsync("Sensitive", 5);
        var client = await CreateClientForAsync(await SeedUserAsync(), NewIp());
        await Book(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Book(client)).StatusCode);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.PostAsync(UploadPath, Form())).StatusCode);
    }

    [Theory]
    [InlineData("Login", "POST", "/api/auth/login")]
    [InlineData("Booking", "POST", "/api/bookings/online")]
    [InlineData("Sensitive", "POST", "/api/uploads/image")]
    [InlineData("AiPublic", "GET", "/api/ai/chat/faq")]
    [InlineData("AiUser", "POST", "/api/ai/suggest/booking")]
    [InlineData("AiManagement", "POST", "/api/ai/analytics/summary")]
    public async Task EveryConfiguredPolicy_IsWiredToItsControllers(string policyProperty, string method, string path)
    {
        // Mỗi cấu hình RateLimiting:{policy} phải chặn đúng endpoint gắn policy đó (rate limit chạy trước phân quyền nên request chưa đăng nhập vẫn được đếm)
        using var _ = await LimitAsync(policyProperty, 1);
        var client = await CreateAnonymousClientAsync(NewIp());
        HttpRequestMessage Request() => new(new HttpMethod(method), path) { Content = method != "POST" ? null : path.StartsWith("/api/uploads") ? Form() : Json() };

        using var first = await client.SendAsync(Request());
        using var second = await client.SendAsync(Request());

        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}
