using Microsoft.Extensions.DependencyInjection;
using WireMock.Server;

namespace SmashCourt_BE.Tests.Integration.Http;

/// <summary>
/// Dựng host HTTP của BE một lần cho cả collection (B6), lazily: bộ test cũ không phải trả chi phí khởi động. Database riêng (B5)
/// được tạo từ <c>schema_smashcourt.sql</c> trong cùng container và xóa khi xong; TADSS là WireMock (CT-04).
/// </summary>
public sealed class HttpTestHostFixture : IAsyncLifetime
{
    // xUnit không tiêm fixture của collection vào fixture khác nên nhận fixture DB lúc test gọi GetAsync lần đầu
    private PostgreSqlIntegrationFixture? _database;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databaseName = $"{SmashCourtWebApplicationFactory.DatabasePrefix}{Guid.NewGuid():N}";
    private bool _databaseCreated;
    private SmashCourtWebApplicationFactory? _factory;
    private WireMockServer? _tadss;

    public string DatabaseName => _databaseName;

    /// <summary>Thời gian dựng database riêng + khởi động host lần đầu (ghi vào báo cáo gói, B6).</summary>
    public TimeSpan StartupTime { get; private set; }

    public WireMockServer Tadss => _tadss ?? throw new InvalidOperationException("Host chưa khởi động: gọi GetAsync() trước.");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<SmashCourtWebApplicationFactory> GetAsync(PostgreSqlIntegrationFixture database)
    {
        if (_factory != null)
            return _factory;

        await _gate.WaitAsync();
        try
        {
            if (_factory != null)
                return _factory;

            _database = database;
            var started = System.Diagnostics.Stopwatch.StartNew();
            var connectionString = await database.CreateIsolatedDatabaseAsync(_databaseName);
            _databaseCreated = true;
            _tadss = WireMockServer.Start();

            var factory = new SmashCourtWebApplicationFactory(connectionString, _tadss.Url!);
            _ = factory.Server; // ép dựng host ngay để lỗi khởi động hiện ở đây
            await WarmUpTadssClientAsync(factory);
            started.Stop();
            StartupTime = started.Elapsed;
            _factory = factory;
            return factory;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Lần gọi HTTP đầu tiên (JIT, kết nối, bộ tuần tự hóa) chậm hơn TimeoutMs của host test; làm nóng một lần để test không bị cắt oan
    private async Task WarmUpTadssClientAsync(SmashCourtWebApplicationFactory factory)
    {
        _tadss!.Given(WireMock.RequestBuilders.Request.Create().WithPath("/api/v2/decisions").UsingPost())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(500));
        factory.SetSetting("TadssConfig:EnableTadssDecision", "true");
        try
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SmashCourt_BE.Integrations.Tadss.ITadssClient>()
                .EvaluateAsync(SmashCourt_BE.Tests.TestData.TadssTestDataAccessor.Request(), Guid.NewGuid());
        }
        finally
        {
            factory.SetSetting("TadssConfig:EnableTadssDecision", null);
            // WireMock ghi log sau khi trả lời: chờ đủ hai lần thử (có thử lại) rồi mới xóa để log muộn không rơi vào test đầu tiên
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (_tadss.LogEntries.Count() < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            _tadss.Reset();
        }
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        _tadss?.Stop();
        _tadss?.Dispose();
        if (_databaseCreated && _database != null)
        {
            try
            {
                await _database.DropIsolatedDatabaseAsync(_databaseName);
            }
            catch (Exception)
            {
                // Container có thể đã bị hủy trước fixture này (thứ tự dispose của xUnit không đảm bảo): DB đi cùng container
            }
        }
        _gate.Dispose();
    }
}
