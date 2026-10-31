using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmashCourt_BE.Data;
using SmashCourt_BE.Models.Enums;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace SmashCourt_BE.Tests.Integration.Invariants.Staging;

/// <summary>
/// Lượt kiểm SC6 staging chạy thật (plan 6A-6 Task 11) trên database riêng của Testcontainers đã nạp schema BE và một bảng
/// <c>tadss.decision_executions</c> tối thiểu, TADSS giả là WireMock. Thay cho lần "chạy thử với SC6_CONNECTION_STRING trỏ DB Testcontainers"
/// của plan: cùng đường đi (dựng context từ chuỗi kết nối, snapshot, gọi TADSS, ghi JSON) nhưng tự động và lặp lại được.
/// Cả lớp dùng chung một database (mỗi database là một data source, tức một service provider nội bộ của EF, giới hạn 20 mỗi tiến trình);
/// mỗi test một khoảng ngày riêng cách nhau 5 ngày nên dữ liệu và quyết định của test khác luôn nằm ngoài khoảng.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Catalog", "BE-IT-20")]
public sealed class StagingInvariantRunnerTests(PostgreSqlIntegrationFixture fixture) : IAsyncLifetime
{
    private const string SharedDatabase = "sc6_staging_shared";
    private static readonly DateTime BaseDay = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly SemaphoreSlim SharedGate = new(1, 1);
    private static string? _sharedConnectionString;
    private static int _windows;

    // Khoảng của test: [BaseDay + 5k; + 1 ngày], chạy kiểm lúc + 2 ngày
    private readonly DateTime _from = BaseDay.AddDays(5 * Interlocked.Increment(ref _windows));
    private DateTime From => _from;
    private DateTime To => _from.AddDays(1);
    private DateTime Now => _from.AddDays(2);
    private DateTime InWindow => _from.AddHours(10);

    private const string TadssTableSql =
        "CREATE SCHEMA tadss; CREATE TABLE tadss.decision_executions (decision_id uuid NOT NULL UNIQUE, reservation_id uuid NOT NULL, " +
        "role varchar(10) NOT NULL, created_at timestamptz NOT NULL);";

    private readonly string _output = Path.Combine(Path.GetTempPath(), $"sc6-{Guid.NewGuid():N}.json");
    private readonly WireMockServer _tadss = WireMockServer.Start();
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        await SharedGate.WaitAsync();
        try
        {
            if (_sharedConnectionString == null)
            {
                _sharedConnectionString = await fixture.CreateIsolatedDatabaseAsync(SharedDatabase);
                await fixture.ExecuteSqlInIsolatedDatabaseAsync(SharedDatabase, TadssTableSql);
            }
            _connectionString = _sharedConnectionString;
        }
        finally
        {
            SharedGate.Release();
        }
    }

    public Task DisposeAsync()
    {
        _tadss.Stop();
        if (File.Exists(_output))
            File.Delete(_output);
        return Task.CompletedTask;
    }

    private SmashCourtContext OpenDb() =>
        new(new DbContextOptionsBuilder<SmashCourtContext>().UseNpgsql(StagingInvariantRunner.DataSourceFor(_connectionString)).Options);

    private StagingRunOptions Options(Uri? tadss = null, bool withTadss = true) =>
        new(_connectionString, From, To, withTadss ? tadss ?? new Uri(_tadss.Url + "/") : null, "sc6-test-key", _output)
        {
            Clock = () => Now,
            DecisionTimeout = TimeSpan.FromSeconds(30) // kiểm phân loại, không kiểm tốc độ máy
        };

    // Đơn đã trả đủ cọc qua luồng TADSS, tạo trong khoảng kiểm
    private async Task<Guid> SeedPaidAsync(Guid? decisionId, DateTime? createdAt = null)
    {
        await using var db = OpenDb();
        var seeder = new TestDataSeeder(db);
        var seed = await seeder.SeedBranchWithCourtAsync();
        var customer = await seeder.SeedCustomerAsync();
        var at = createdAt ?? InWindow;
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PAID_ONLINE, at);
        booking.TadssDecisionId = decisionId;
        db.Bookings.Add(booking);
        var invoice = TestDataSeeder.NewInvoice(booking);
        invoice.DepositRate = 1m;
        invoice.DepositAmount = invoice.FinalTotal;
        invoice.DepositSource = DepositSource.TADSS;
        invoice.PaymentStatus = InvoicePaymentStatus.PARTIALLY_PAID;
        db.Invoices.Add(invoice);
        db.Payments.Add(TestDataSeeder.NewPayment(invoice.Id, PaymentTxStatus.SUCCESS, invoice.FinalTotal));
        db.BookingCourts.Add(TestDataSeeder.NewBookingCourt(booking.Id, seed.Court.Id, new DateOnly(2026, 12, 10), new TimeOnly(18, 0), new TimeOnly(19, 0)));
        await db.SaveChangesAsync();
        return booking.Id;
    }

    // Đơn PENDING thiếu hold_deadline (PHU-2)
    private async Task<Guid> SeedBrokenAsync(DateTime createdAt)
    {
        await using var db = OpenDb();
        var seeder = new TestDataSeeder(db);
        var seed = await seeder.SeedBranchWithCourtAsync();
        var customer = await seeder.SeedCustomerAsync();
        var booking = TestDataSeeder.NewBooking(seed.Branch.Id, customer.Id, BookingStatus.PENDING, createdAt);
        db.Bookings.Add(booking);
        db.Invoices.Add(TestDataSeeder.NewInvoice(booking));
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private Task InsertExecutionAsync(Guid decisionId, Guid reservationId, DateTime createdAt, string role = "PRIMARY") =>
        fixture.ExecuteSqlInIsolatedDatabaseAsync(SharedDatabase,
            "INSERT INTO tadss.decision_executions (decision_id, reservation_id, role, created_at) VALUES " +
            $"('{decisionId}', '{reservationId}', '{role}', '{createdAt:yyyy-MM-ddTHH:mm:ss}Z');");

    private void ReplyFound(Guid decisionId) =>
        _tadss.Given(Request.Create().WithPath($"/api/v2/decisions/{decisionId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{"decisionId":"{{decisionId}}"}"""));

    private void ReplyMissing(Guid decisionId) =>
        _tadss.Given(Request.Create().WithPath($"/api/v2/decisions/{decisionId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404).WithHeader("Content-Type", "application/problem+json")
                .WithBody("""{"title":"Not found","status":404,"code":"DECISION_NOT_FOUND"}"""));

    private async Task<string> WrittenStatusAsync()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(_output));
        return json.RootElement.GetProperty("status").GetString() ?? "";
    }

    // ───────────────────────── Kết luận ─────────────────────────

    [Fact]
    public async Task EverythingConsistent_Pass_AndTheReportIsWritten()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedPaidAsync(a);
        await SeedPaidAsync(b);
        await SeedPaidAsync(decisionId: null); // cờ tắt: không có quyết định, không gọi TADSS
        ReplyFound(a);
        ReplyFound(b);

        var report = await StagingInvariantRunner.RunAsync(Options());

        Assert.Equal(StagingInvariantReport.Pass, report.Status);
        Assert.Equal(3, report.CheckedBookings);
        Assert.Equal(2, report.CheckedDecisions);
        Assert.Equal(Now, report.StartedAt);
        Assert.Equal("PASS", await WrittenStatusAsync());
        Assert.All(_tadss.LogEntries, e => Assert.Equal("sc6-test-key", e.RequestMessage?.Headers?["X-Api-Key"].ToString()));
        Assert.Equal(2, _tadss.LogEntries.Count());
    }

    [Fact]
    public async Task DecisionMissingAtTadss_Fail_Sc65OnThatBooking()
    {
        var found = Guid.NewGuid();
        var missing = Guid.NewGuid();
        await SeedPaidAsync(found);
        var broken = await SeedPaidAsync(missing);
        ReplyFound(found);
        ReplyMissing(missing);

        var report = await StagingInvariantRunner.RunAsync(Options());

        Assert.Equal(StagingInvariantReport.Fail, report.Status);
        var violation = Assert.Single(report.Violations);
        Assert.Equal(InvariantRules.DecisionExistsInTadss, violation.Rule);
        Assert.Equal(broken, violation.BookingId);
        Assert.Contains(missing.ToString(), violation.Detail);
        Assert.Equal("FAIL", await WrittenStatusAsync());
    }

    [Fact]
    public async Task TadssUnavailable_Inconclusive_NeverPass()
    {
        var decision = Guid.NewGuid();
        await SeedPaidAsync(decision);
        _tadss.Given(Request.Create().UsingGet()).RespondWith(Response.Create().WithStatusCode(503));

        var report = await StagingInvariantRunner.RunAsync(Options());

        Assert.Equal(StagingInvariantReport.Inconclusive, report.Status);
        var check = Assert.Single(report.InconclusiveChecks);
        Assert.Equal(decision, check.DecisionId);
        Assert.Equal("HTTP 503", check.Reason);
    }

    [Fact]
    public async Task WithoutTadssUrl_DecisionsAreInconclusive_ButNoDecisionsStillPasses()
    {
        await SeedPaidAsync(decisionId: null);
        Assert.Equal(StagingInvariantReport.Pass, (await StagingInvariantRunner.RunAsync(Options(withTadss: false))).Status);

        await SeedPaidAsync(Guid.NewGuid());
        var report = await StagingInvariantRunner.RunAsync(Options(withTadss: false));

        Assert.Equal(StagingInvariantReport.Inconclusive, report.Status);
        Assert.Equal("thiếu TADSS_BASE_URL", Assert.Single(report.InconclusiveChecks).Reason);
    }

    [Fact]
    public async Task Sc6ViolationInTheWindow_Fail_OutsideTheWindowIgnored()
    {
        var inside = await SeedBrokenAsync(InWindow);
        await SeedBrokenAsync(From.AddDays(-2)); // dữ liệu cũ của staging

        var report = await StagingInvariantRunner.RunAsync(Options());

        Assert.Equal(StagingInvariantReport.Fail, report.Status);
        var violation = Assert.Single(report.Violations);
        Assert.Equal(inside, violation.BookingId);
        Assert.Equal(InvariantRules.PendingHasHoldDeadline, violation.Rule);
        Assert.Equal(1, report.CheckedBookings);
    }

    // ───────────────────────── Tập dữ liệu cố định (R28) ─────────────────────────

    [Fact]
    public async Task WindowNotFinishedOrReversed_DoesNotRunAndWritesNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => StagingInvariantRunner.RunAsync(Options() with { ToUtc = Now.AddSeconds(1) }));
        await Assert.ThrowsAsync<ArgumentException>(() => StagingInvariantRunner.RunAsync(Options() with { FromUtc = To.AddTicks(1) }));
        Assert.False(File.Exists(_output));
    }

    [Fact]
    public async Task BookingsCreatedWhileTadssIsBeingCalled_AreNotAddedToTheCheckedSet()
    {
        var decision = Guid.NewGuid();
        await SeedPaidAsync(decision);
        ReplyFound(decision);
        // Trong lúc gọi TADSS, kịch bản tạo thêm một đơn có quyết định nằm trong khoảng: không được kiểm thêm
        var handler = new OnFirstRequest(new HttpClientHandler(), () => SeedPaidAsync(Guid.NewGuid()));

        var report = await StagingInvariantRunner.RunAsync(Options() with { TadssHandler = handler });

        Assert.True(handler.Fired);
        Assert.Equal(StagingInvariantReport.Pass, report.Status);
        Assert.Equal(1, report.CheckedDecisions);
        Assert.Equal(1, report.CheckedBookings);
    }

    // ───────────────────────── Lỗi giữa chừng ─────────────────────────

    [Fact]
    public async Task UnreachableDatabase_WritesInconclusiveWithTheReason()
    {
        var wrongDatabase = new NpgsqlConnectionStringBuilder(_connectionString) { Database = SharedDatabase + "_khong_co" }.ConnectionString;

        var report = await StagingInvariantRunner.RunAsync(Options() with { ConnectionString = wrongDatabase });

        Assert.Equal(StagingInvariantReport.Inconclusive, report.Status);
        Assert.Contains("PostgresException", report.AbortReason);
        Assert.Equal("INCONCLUSIVE", await WrittenStatusAsync());
    }

    [Fact]
    public async Task CancelledRun_IsInconclusive_NeverPass()
    {
        await SeedPaidAsync(Guid.NewGuid());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var report = await StagingInvariantRunner.RunAsync(Options(), cts.Token);

        Assert.Equal(StagingInvariantReport.Inconclusive, report.Status);
        Assert.NotNull(report.AbortReason);
        Assert.Equal("INCONCLUSIVE", await WrittenStatusAsync());
    }

    // ───────────────────────── Tài nguyên dùng lại giữa các lượt ─────────────────────────

    // Hồi quy: mỗi data source mới là một service provider nội bộ của EF; quá 20 trong một tiến trình EF ném
    // ManyServiceProvidersCreatedWarning và mọi DbContext sau đó hỏng (lỗi từng làm 449 test của cả suite đỏ, chỉ lộ khi chạy toàn bộ).
    // 25 lượt liên tiếp vượt ngưỡng ngay trong lượt chạy riêng của lớp này, nên lỗi hiện ra mà không cần chạy cả suite
    [Fact]
    public async Task ManyRunsOnOneDatabase_ReuseOneDataSource_AndEveryRunPasses()
    {
        Assert.Same(StagingInvariantRunner.DataSourceFor(_connectionString), StagingInvariantRunner.DataSourceFor(_connectionString));

        for (var run = 0; run < 25; run++)
        {
            var report = await StagingInvariantRunner.RunAsync(Options() with { OutputPath = null });
            Assert.True(report.Status == StagingInvariantReport.Pass, $"lượt {run + 1}: {report.Status} {report.AbortReason}");
        }
    }

    // ───────────────────────── Quyết định mồ côi (chẩn đoán) ─────────────────────────

    [Fact]
    public async Task OrphanDecisions_AreListedWithoutChangingTheStatus()
    {
        var stored = Guid.NewGuid();
        var withDecision = await SeedPaidAsync(stored);
        var withoutDecision = await SeedPaidAsync(decisionId: null);
        ReplyFound(stored);
        var otherForStored = Guid.NewGuid();
        var forMissingBooking = Guid.NewGuid();
        var forUnsaved = Guid.NewGuid();
        await InsertExecutionAsync(stored, withDecision, InWindow);                  // khớp: không mồ côi
        await InsertExecutionAsync(otherForStored, withDecision, InWindow);          // đơn lưu quyết định khác
        await InsertExecutionAsync(forUnsaved, withoutDecision, InWindow);           // đơn không lưu quyết định
        await InsertExecutionAsync(forMissingBooking, Guid.NewGuid(), InWindow);     // BE không có đơn
        await InsertExecutionAsync(Guid.NewGuid(), withoutDecision, InWindow, "SHADOW");
        await InsertExecutionAsync(Guid.NewGuid(), withoutDecision, From.AddDays(-1));

        var report = await StagingInvariantRunner.RunAsync(Options());

        Assert.Equal(StagingInvariantReport.Pass, report.Status);
        Assert.Null(report.OrphanCheckError);
        Assert.Equal(
            new[] { otherForStored, forUnsaved, forMissingBooking }.Order(),
            report.OrphanDecisions.Select(o => o.DecisionId).Order());
        Assert.Contains(report.OrphanDecisions, o => o.DecisionId == forUnsaved && o.Reason == "đơn BE không lưu tadss_decision_id");
        Assert.Contains(report.OrphanDecisions, o => o.DecisionId == forMissingBooking && o.Reason == "BE không có đơn này");
        Assert.Contains(report.OrphanDecisions, o => o.DecisionId == otherForStored && o.Reason.StartsWith("đơn BE lưu quyết định khác"));
    }

    // Bảng của TADSS không đọc được (sai quyền, khác database): chỉ mất phần chẩn đoán, các điều của SC6 vẫn được kiểm
    [Fact]
    public async Task TadssTableUnreadable_OrphanCheckErrorButSc6StillChecked()
    {
        // Database riêng không có schema tadss (database chung của lớp có bảng đó)
        const string withoutTadss = "sc6_staging_without_tadss";
        _connectionString = await fixture.CreateIsolatedDatabaseAsync(withoutTadss);
        try
        {
            var broken = await SeedBrokenAsync(InWindow);

            var report = await StagingInvariantRunner.RunAsync(Options());

            Assert.Equal(StagingInvariantReport.Fail, report.Status);
            Assert.Equal(broken, Assert.Single(report.Violations).BookingId);
            Assert.StartsWith("Không đọc được tadss.decision_executions", report.OrphanCheckError);
            Assert.Null(report.AbortReason);
        }
        finally
        {
            await fixture.DropIsolatedDatabaseAsync(withoutTadss);
        }
    }

    /// <summary>Chạy một việc đúng một lần khi request đầu tiên tới TADSS, rồi mới gửi request đó.</summary>
    private sealed class OnFirstRequest(HttpMessageHandler inner, Func<Task> action) : DelegatingHandler(inner)
    {
        private int _fired;

        public bool Fired => _fired == 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
                await action();
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
