using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SmashCourt_BE.Data;
using SmashCourt_BE.Extensions;
using SmashCourt_BE.Repositories;
using Xunit.Abstractions;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// P6-54 trên PostgreSQL 17 thật, cấu hình kết nối của môi trường kiểm thử và đánh giá perf (MaxAutoPrepare 50; bản deploy không bật): câu lặp lại được prepare và chạy bằng kế hoạch chung,
/// kết quả không đổi, giữ ít khóa hơn (lý do bật), câu đã prepare còn nguyên khi kết nối quay về pool, bỏ index không làm lỗi câu đã prepare
/// (script P6-53), còn đổi kiểu cột đang đọc thì lỗi ⇒ quy tắc vận hành "chạy script đổi schema xong khởi động lại BE".
/// </summary>
[Trait("Category", "Integration")]
[Trait("Catalog", "OPS-06")]
public sealed class NpgsqlAutoPrepareTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output) : BookingDepositTestBase(fixture)
{
    // Npgsql prepare từ lần dùng thứ 5 (Auto Prepare Min Usages), PostgreSQL chạy câu đã prepare bằng kế hoạch riêng 5 lần rồi mới cân nhắc kế hoạch chung
    private const int Runs = 15;

    private const string RelationLocksSql =
        "SELECT count(*) FROM pg_locks WHERE pid = pg_backend_pid() AND locktype = 'relation' AND relation <> 'pg_locks'::regclass";

    private static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    protected override IServiceScope CreateScope() => Fixture.CreateEvaluationScope();

    // Data source riêng cùng cấu hình kết nối của perf, một kết nối vật lý để mọi lần mở đều về đúng backend
    private NpgsqlDataSource SingleConnectionDataSource() =>
        new NpgsqlDataSourceBuilder($"{Fixture.ConnectionString};Maximum Pool Size=1")
            .MapSmashCourtEnums()
            .UseSmashCourtAutoPrepare(PostgreSqlIntegrationFixture.EvaluationDatabaseSettings)
            .Build();

    [Fact]
    public async Task GetByIdWithDetailsAsync_WhenRepeated_RunsOnGenericPlanAndReturnsSameBooking()
    {
        var order = await SeedOrderAsync();
        DbContext.ChangeTracker.Clear();

        for (var run = 0; run < Runs; run++)
        {
            var booking = await Repository.GetByIdWithDetailsAsync(order.Booking.Id);
            Assert.NotNull(booking);
            Assert.Equal(order.Booking.Id, booking.Id);
            Assert.Equal(order.Courts.Count, booking.BookingCourts.Count);
            Assert.Equal(order.Invoice.Id, booking.Invoice!.Id);
            DbContext.ChangeTracker.Clear();
        }

        var connection = (NpgsqlConnection)DbContext.Database.GetDbConnection();
        var transaction = (NpgsqlTransaction)DbContext.Database.CurrentTransaction!.GetDbTransaction();
        var genericPlans = await ScalarAsync(connection,
            "SELECT coalesce(max(generic_plans), 0) FROM pg_prepared_statements " +
            "WHERE statement LIKE '%booking_promotions%' AND statement LIKE '%refunds%' AND statement LIKE '%time_slots%'", transaction);
        Assert.True(genericPlans > 0, "Câu chi tiết booking chưa chạy bằng kế hoạch chung");
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_OnGenericPlan_HoldsFewerRelationLocks()
    {
        await using var dataSource = SingleConnectionDataSource();
        await using var db = new SmashCourtContext(new DbContextOptionsBuilder<SmashCourtContext>().UseNpgsql(dataSource).Options);
        var repository = new BookingRepository(db);
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        // Mỗi lần một transaction riêng: khóa giữ tới hết transaction nên đếm được khóa của đúng một câu
        var locks = new List<long>();
        for (var run = 0; run < Runs; run++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await repository.GetByIdWithDetailsAsync(Guid.NewGuid());
            locks.Add(await ScalarAsync(connection, RelationLocksSql, (NpgsqlTransaction)transaction.GetDbTransaction()));
            await transaction.CommitAsync();
        }

        output.WriteLine($"Khóa quan hệ theo lần chạy: {string.Join(", ", locks)}");

        // Lần đầu lập kế hoạch khóa mọi index của 12 bảng; kế hoạch chung chỉ khóa bảng và index được dùng
        Assert.True(locks[^1] * 2 <= locks[0], $"Khóa theo lần chạy: {string.Join(", ", locks)}");
    }

    [Fact]
    public async Task PreparedStatement_WhenConnectionReturnsToPool_IsKeptAndStillCorrect()
    {
        var slot = await SeedCommittedTimeSlotAsync();
        await using var dataSource = SingleConnectionDataSource();
        const string query = "SELECT count(*) FROM time_slots WHERE id = $1";

        long firstPid;
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            firstPid = await ScalarAsync(connection, "SELECT pg_backend_pid()");
            for (var run = 0; run < Runs; run++)
                Assert.Equal(1, await CountAsync(connection, query, slot));
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            Assert.Equal(firstPid, await ScalarAsync(connection, "SELECT pg_backend_pid()"));
            Assert.Equal(1, await ScalarAsync(connection, "SELECT count(*) FROM pg_prepared_statements WHERE statement = 'SELECT count(*) FROM time_slots WHERE id = $1'"));
            Assert.Equal(1, await CountAsync(connection, query, slot));
            Assert.Equal(0, await CountAsync(connection, query, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task PreparedStatement_WhenIndexItUsesIsDropped_ReplansWithoutError()
    {
        await using var dataSource = SingleConnectionDataSource();
        await using var connection = await dataSource.OpenConnectionAsync();
        var table = await CreateProbeTableAsync(connection);
        try
        {
            await ScalarAsync(connection, $"CREATE INDEX {table}_val ON {table} (val)");
            var query = $"SELECT count(*) FROM {table} WHERE val = $1";
            for (var run = 0; run < Runs; run++)
                Assert.Equal(1, await CountAsync(connection, query, 7));

            // Như script phase6_drop_duplicate_booking_index.sql: PostgreSQL tự bỏ kế hoạch cũ và lập lại
            await ScalarAsync(connection, $"DROP INDEX {table}_val");
            Assert.Equal(1, await CountAsync(connection, query, 7));
        }
        finally
        {
            await ScalarAsync(connection, $"DROP TABLE {table}");
        }
    }

    [Fact]
    public async Task PreparedStatement_WhenReadColumnTypeChanges_FailsSoSchemaScriptsRequireBeRestart()
    {
        await using var dataSource = SingleConnectionDataSource();
        await using var connection = await dataSource.OpenConnectionAsync();
        var table = await CreateProbeTableAsync(connection);
        try
        {
            var query = $"SELECT val FROM {table} WHERE id = $1";
            for (var run = 0; run < Runs; run++)
                Assert.Equal(7L, await ScalarWithParameterAsync(connection, query, 1));

            await ScalarAsync(connection, $"ALTER TABLE {table} ALTER COLUMN val TYPE bigint");

            var error = await Assert.ThrowsAsync<PostgresException>(() => ScalarWithParameterAsync(connection, query, 1));
            Assert.Equal(PostgresErrorCodes.FeatureNotSupported, error.SqlState); // cached plan must not change result type
        }
        finally
        {
            await ScalarAsync(connection, $"DROP TABLE {table}");
        }
    }

    private static async Task<long> CountAsync<T>(NpgsqlConnection connection, string sql, T value)
    {
        await using var command = new NpgsqlCommand(sql, connection) { Parameters = { new NpgsqlParameter<T> { TypedValue = value } } };
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ScalarWithParameterAsync(NpgsqlConnection connection, string sql, int id)
    {
        await using var command = new NpgsqlCommand(sql, connection) { Parameters = { new NpgsqlParameter<int> { TypedValue = id } } };
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    // Bảng thử riêng (tên ngẫu nhiên, xóa trong finally): không đụng bảng của schema dùng chung
    private static async Task<string> CreateProbeTableAsync(NpgsqlConnection connection)
    {
        var table = $"autoprep_probe_{Guid.NewGuid():N}";
        await ScalarAsync(connection, $"CREATE TABLE {table} (id int PRIMARY KEY, val int NOT NULL)");
        await ScalarAsync(connection, $"INSERT INTO {table} SELECT i, i + 6 FROM generate_series(1, 200) i");
        await ScalarAsync(connection, $"ANALYZE {table}");
        return table;
    }

    // Dữ liệu của test chính nằm trong transaction chưa commit nên kết nối khác không thấy; khung giờ này commit qua scope riêng
    private async Task<Guid> SeedCommittedTimeSlotAsync()
    {
        using var scope = Fixture.CreateScope();
        var seeder = new TestDataSeeder(scope.ServiceProvider.GetRequiredService<SmashCourtContext>());
        var slot = await seeder.SeedTimeSlotAsync(new TimeOnly(5, 0), new TimeOnly(5, 30));
        return slot.Id;
    }
}
