using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using Npgsql;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>Task 1 của 6A-2a: host thật khởi động trên database riêng, không nối nhầm DB, không có Hangfire.</summary>
[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class HttpHostSmokeTests(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : HttpTestBase(database, host)
{
    [Fact]
    public async Task Host_Starts_AndAnswersAPublicGetWithTheApiEnvelope()
    {
        var client = await CreateClientAsync();

        var response = await client.GetAsync("/api/branches/basic");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Host_WiresVnPayAsTheOnlyPaymentGateway()
    {
        // Program.cs: mỗi cổng một IPaymentGateway; thiếu hoặc trùng đăng ký chỉ lộ ra ở request thanh toán đầu tiên
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<SmashCourt_BE.Services.IService.IPaymentGatewayResolver>();

        Assert.IsType<SmashCourt_BE.Services.VnPayService>(resolver.Get(SmashCourt_BE.Models.Enums.PaymentTxMethod.VNPAY));
        Assert.Throws<InvalidOperationException>(() => resolver.Get(SmashCourt_BE.Models.Enums.PaymentTxMethod.MOMO));
    }

    [Fact]
    public async Task Host_UsesTheDedicatedDatabaseNotTheSharedOne()
    {
        // B2: Program.cs đọc chuỗi kết nối rất sớm; phải chứng minh host nối đúng database riêng của bộ HTTP
        var current = await WithDbAsync(async db =>
        {
            await using var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT current_database()", connection);
            return (string)(await command.ExecuteScalarAsync())!;
        });

        Assert.Equal(Host.DatabaseName, current);
        Assert.StartsWith(SmashCourtWebApplicationFactory.DatabasePrefix, current);
        Assert.NotEqual("smashcourt_test", current);
    }

    [Fact]
    public async Task Host_WritesCommitForRealAndAreVisibleFromAnotherScope()
    {
        // Khác test rollback: dữ liệu của bộ HTTP bền qua các scope
        var name = $"Loyalty-{Guid.NewGuid():N}";
        await WithDbAsync(async db =>
        {
            db.LoyaltyTiers.Add(new SmashCourt_BE.Models.Entities.LoyaltyTier
            {
                Id = Guid.NewGuid(), Name = name, MinPoints = Random.Shared.Next(1_000_000, 2_000_000_000), DiscountRate = 0, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var seen = await WithDbAsync(db => db.LoyaltyTiers.AnyAsync(t => t.Name == name));

        Assert.True(seen);
    }

    [Fact]
    public async Task Host_DoesNotRegisterHangfire()
    {
        // D4: Hangfire:Enabled=false ⇒ không có server, không có client, không có schema hangfire
        var factory = await GetFactoryAsync();

        Assert.Null(factory.Services.GetService<Hangfire.IBackgroundJobClient>());
        Assert.DoesNotContain(factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
            s => s.GetType().Name.Contains("BackgroundJobServer", StringComparison.Ordinal));

        // Schema hangfire có sẵn trong bản dump (DB production có Hangfire) nên chỉ kiểm host không ghi gì vào đó:
        // không server đăng ký, không job định kỳ (lưu ở hash/set), không job nào được xếp hàng
        var rows = await WithDbAsync(async db =>
        {
            await using var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT (SELECT count(*) FROM hangfire.server) + (SELECT count(*) FROM hangfire.hash) + " +
                "(SELECT count(*) FROM hangfire.set) + (SELECT count(*) FROM hangfire.job)", connection);
            return (long)(await command.ExecuteScalarAsync())!;
        });
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task Host_ReplacesEmailWithARecorderAndNeverTouchesSmtp()
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();

        var email = scope.ServiceProvider.GetRequiredService<SmashCourt_BE.Services.EmailService>();

        Assert.Same(factory.Emails, email);
    }

    [Fact]
    public async Task Host_StartupTimeIsRecordedAndStaysWithinTheBudget()
    {
        await GetFactoryAsync();

        // Ngân sách của plan: khởi động host < 30 s khi image đã có (giá trị đo thật ghi vào báo cáo gói)
        Assert.InRange(Host.StartupTime, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("Host=db.example.com;Database=smashcourt_http_x;Username=u;Password=p")]
    [InlineData("Host=ep-cool-123.neon.tech;Database=neondb;Username=u;Password=p")]
    [InlineData("Host=localhost;Database=smashcourt_test;Username=u;Password=p")]
    [InlineData("Host=localhost;Database=neondb;Username=u;Password=p")]
    [InlineData("Host=localhost;Username=u;Password=p")]
    public void Factory_RefusesAConnectionStringThatIsNotTheLocalHttpDatabase(string connectionString)
    {
        // B2: không bao giờ nối nhầm DB thật hay DB dùng chung của test rollback
        Assert.Throws<InvalidOperationException>(() => new SmashCourtWebApplicationFactory(connectionString, "http://127.0.0.1:1"));
    }

    [Theory]
    [InlineData("Host=localhost;Database=smashcourt_http_abc;Username=u;Password=p")]
    [InlineData("Host=127.0.0.1;Port=5555;Database=smashcourt_http_abc;Username=u;Password=p")]
    public void AssertSafeConnectionString_AcceptsLocalHttpDatabases(string connectionString)
    {
        SmashCourtWebApplicationFactory.AssertSafeConnectionString(connectionString);
    }

    [Fact]
    public void Factory_WhenRefused_DoesNotLeakEnvironmentVariables()
    {
        var before = Environment.GetEnvironmentVariable("Jwt__Key");

        Assert.Throws<InvalidOperationException>(() => new SmashCourtWebApplicationFactory("Host=evil;Database=x", "http://127.0.0.1:1"));

        Assert.Equal(before, Environment.GetEnvironmentVariable("Jwt__Key"));
    }
}
