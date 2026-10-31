using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Tests.Integration.Invariants;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Nền cho test HTTP: gọi endpoint thật của BE trên database riêng. Dữ liệu commit thật (không rollback) nên mỗi test seed định danh
/// mới (GUID), không đếm cả bảng và không phụ thuộc thứ tự test (B5).
/// Sau mỗi test tự chạy bộ kiểm SC6 (BE-IT-20, plan 6A-6 Task 10) trên các đơn test sinh ra hoặc sửa: mọi luồng HTTP (đặt, duyệt, hủy,
/// IPN, job, check-out) tự được kiểm mà không phụ thuộc thứ tự test. Phạm vi so với ảnh chụp (id, updated_at) lúc test bắt đầu, không so
/// theo đồng hồ, nên đơn test trước vừa sửa không lọt sang test sau.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class HttpTestBase(PostgreSqlIntegrationFixture database, HttpTestHostFixture host) : IAsyncLifetime
{
    private Dictionary<Guid, DateTime> _bookingsAtStart = [];

    protected HttpTestHostFixture Host { get; } = host;

    /// <summary>
    /// Chỉ override khi test <b>cố ý</b> để lại dữ liệu vi phạm SC6 trong DB, kèm lý do; test kiểm DB từ chối một thao tác thì không cần
    /// (thao tác thất bại không để lại dữ liệu). Mọi test bỏ qua được liệt kê ở kết quả Task 10 của plan 6A-6.
    /// </summary>
    protected virtual string? SkipInvariantCheckReason => null;

    /// <summary>Chụp (id, updated_at) của mọi đơn; lớp con override phải gọi base.</summary>
    public virtual async Task InitializeAsync() => _bookingsAtStart = await ReadBookingVersionsAsync();

    /// <summary>Chụp lại mốc giữa chừng: đơn đã dựng trước lời gọi này được coi là "đơn có sẵn" (chỉ dùng để test chính bộ tự kiểm).</summary>
    protected async Task RestartInvariantScopeAsync() => _bookingsAtStart = await ReadBookingVersionsAsync();

    /// <summary>Tự kiểm SC6 trên đơn test sinh ra hoặc sửa; có vi phạm thì test fail kèm danh sách. Lớp con override phải gọi base.</summary>
    public virtual async Task DisposeAsync()
    {
        if (SkipInvariantCheckReason != null)
            return;

        var violations = await CheckTouchedBookingsAsync();
        if (violations.Count > 0)
            throw new InvariantViolationException(violations);
    }

    /// <summary>
    /// Đơn mới sinh trong test ∪ đơn có sẵn mà <c>updated_at</c> đã đổi (bị test hủy, duyệt, check-out…). Đường ghi đổi trạng thái
    /// hoặc dữ liệu giữ sân mà không ghi <c>updated_at</c> sẽ lọt khỏi phạm vi: rà ở R30 (plan 6A-6 Task 10).
    /// </summary>
    protected async Task<IReadOnlyCollection<Guid>> TouchedBookingIdsAsync()
    {
        var now = await ReadBookingVersionsAsync();
        return now
            .Where(b => !_bookingsAtStart.TryGetValue(b.Key, out var updatedAt) || updatedAt != b.Value)
            .Select(b => b.Key)
            .ToHashSet();
    }

    /// <summary>Bộ kiểm SC6 trên phạm vi tự kiểm, "now" là đồng hồ hệ thống như host.</summary>
    protected async Task<IReadOnlyList<InvariantViolation>> CheckTouchedBookingsAsync()
    {
        var scope = await TouchedBookingIdsAsync();
        return await WithDbAsync(db => BookingInvariantChecker.CheckAsync(db, BookingScope.Of(scope), DateTime.UtcNow));
    }

    private Task<Dictionary<Guid, DateTime>> ReadBookingVersionsAsync() =>
        WithDbAsync(db => db.Bookings.AsNoTracking().Select(b => new { b.Id, b.UpdatedAt }).ToDictionaryAsync(b => b.Id, b => b.UpdatedAt));

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
