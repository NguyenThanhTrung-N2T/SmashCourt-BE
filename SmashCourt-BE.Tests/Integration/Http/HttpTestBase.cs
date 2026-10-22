using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Nền cho test HTTP: gọi endpoint thật của BE trên database riêng. Dữ liệu commit thật (không rollback) nên mỗi test seed định danh
/// mới (GUID), không đếm cả bảng và không phụ thuộc thứ tự test (B5).
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class HttpTestBase(PostgreSqlIntegrationFixture database, HttpTestHostFixture host)
{
    protected HttpTestHostFixture Host { get; } = host;

    protected Task<SmashCourtWebApplicationFactory> GetFactoryAsync() => Host.GetAsync(database);

    protected async Task<HttpClient> CreateClientAsync() => (await GetFactoryAsync()).CreateClient();

    /// <summary>TADSS giả đã được reset; dùng ở đầu test cần kịch bản.</summary>
    protected async Task<TadssStub> GetTadssAsync()
    {
        await GetFactoryAsync();
        var stub = new TadssStub(Host.Tadss);
        stub.Reset();
        return stub;
    }

    /// <summary>Chạy một đoạn trên DbContext của host (cùng database với HTTP), commit thật.</summary>
    protected async Task<T> WithDbAsync<T>(Func<SmashCourtContext, Task<T>> action)
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());
    }

    /// <summary>Seed một người dùng mới (email duy nhất) với vai trò và trạng thái cho trước.</summary>
    protected Task<User> SeedUserAsync(UserRole role = UserRole.CUSTOMER, UserStatus status = UserStatus.ACTIVE) =>
        WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@test.local",
                FullName = $"Test {role}",
                Role = role,
                Status = status,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user;
        });

    /// <summary>JWT thật do <c>ITokenService</c> của host phát cho người dùng đã seed (không có đường tắt xác thực trong app).</summary>
    protected async Task<string> TokenForAsync(User user)
    {
        var factory = await GetFactoryAsync();
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user);
    }

    /// <summary>Client gắn sẵn <c>Authorization: Bearer</c> của người dùng; <paramref name="forwardedFor"/> giả lập IP máy khách.</summary>
    protected async Task<HttpClient> CreateClientForAsync(User user, string? forwardedFor = null)
    {
        var client = await CreateClientAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenForAsync(user));
        if (forwardedFor != null)
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    /// <summary>Client chưa đăng nhập; <paramref name="forwardedFor"/> giả lập IP máy khách.</summary>
    protected async Task<HttpClient> CreateAnonymousClientAsync(string? forwardedFor = null)
    {
        var client = await CreateClientAsync();
        if (forwardedFor != null)
            client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }
}
