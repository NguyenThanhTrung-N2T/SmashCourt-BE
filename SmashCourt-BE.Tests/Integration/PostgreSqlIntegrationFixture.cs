using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Npgsql;
using SmashCourt_BE.Data;
using SmashCourt_BE.Extensions;
using Testcontainers.PostgreSql;

namespace SmashCourt_BE.Tests.Integration;

/// <summary>
/// PostgreSQL thật (cùng dòng 17 với Neon) nạp đúng schema production (TestData/Database/schema_smashcourt.sql)
/// nên ràng buộc CHECK, exclusion, enum có mặt trong test (Phase 6, P6-16).
/// Ánh xạ enum dùng chung với app qua MapSmashCourtEnums.
/// </summary>
public sealed class PostgreSqlIntegrationFixture : IAsyncLifetime
{
    // Ghim minor ≥ 17.6: psql phải hiểu \restrict / \unrestrict trong bản dump của pg_dump 17.6+
    private const string Image = "postgres:17.6-alpine";
    private const string SchemaFileInContainer = "/tmp/schema_smashcourt.sql";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("smashcourt_test")
        .WithUsername("test")
        .WithPassword("test123")
        .Build();

    private NpgsqlDataSource? _dataSource;
    private ServiceProvider? _serviceProvider;

    static PostgreSqlIntegrationFixture()
    {
        // Giống Program.cs: ghi timestamptz bắt buộc DateTime Kind = Utc
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);
    }

    /// <summary>Chuỗi kết nối tới DB test (dùng khi test cần chạy SQL thô).</summary>
    public string ConnectionString => _container.GetConnectionString();

    public IServiceScope CreateScope()
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("The PostgreSQL fixture has not been initialized.");

        return _serviceProvider.CreateScope();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await LoadSchemaAsync();

        _dataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString())
            .MapSmashCourtEnums()
            .Build();

        var services = new ServiceCollection();
        services.AddDbContext<SmashCourtContext>(options => options.UseNpgsql(_dataSource));
        _serviceProvider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider != null)
            await _serviceProvider.DisposeAsync();
        if (_dataSource != null)
            await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    // Nạp bằng psql trong container: xử lý đúng meta-command của pg_dump, lỗi nào cũng làm fixture đỏ
    private async Task LoadSchemaAsync()
    {
        var schema = await File.ReadAllBytesAsync(SchemaFilePath);
        // Bỏ BOM nếu có
        if (schema.Length >= 3 && schema[0] == 0xEF && schema[1] == 0xBB && schema[2] == 0xBF)
            schema = schema[3..];
        await _container.CopyAsync(schema, SchemaFileInContainer);

        // Các dòng OWNER TO trong bản dump cần role này
        await RunPsqlAsync("-c", "CREATE ROLE neondb_owner");
        await RunPsqlAsync("-f", SchemaFileInContainer);
    }

    /// <summary>
    /// Tạo một database riêng trong cùng container, nạp lại đúng schema (B5): bộ test HTTP commit dữ liệu thật nên không được dùng chung
    /// DB với các test rollback/quét toàn bảng. Trả chuỗi kết nối tới database mới.
    /// </summary>
    public async Task<string> CreateIsolatedDatabaseAsync(string databaseName)
    {
        EnsureSafeDatabaseName(databaseName);
        await RunPsqlInAsync("smashcourt_test", "-c", $"CREATE DATABASE {databaseName}");
        await RunPsqlInAsync(databaseName, "-f", SchemaFileInContainer);
        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = databaseName }.ConnectionString;
    }

    public async Task DropIsolatedDatabaseAsync(string databaseName)
    {
        EnsureSafeDatabaseName(databaseName);
        NpgsqlConnection.ClearAllPools(); // nhả kết nối đang giữ trong pool trước khi xóa
        await RunPsqlInAsync("smashcourt_test", "-c", $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)");
    }

    /// <summary>Chạy một câu SQL trong database riêng đã tạo bằng <see cref="CreateIsolatedDatabaseAsync"/> (VD dọn schema ngoại lai).</summary>
    public Task ExecuteSqlInIsolatedDatabaseAsync(string databaseName, string sql)
    {
        EnsureSafeDatabaseName(databaseName);
        return RunPsqlInAsync(databaseName, "-c", sql);
    }

    // Tên được ghép vào câu lệnh DDL nên chỉ nhận chữ thường, số, gạch dưới
    private static void EnsureSafeDatabaseName(string databaseName)
    {
        if (!Regex.IsMatch(databaseName, "^[a-z][a-z0-9_]{0,62}$"))
            throw new ArgumentException($"Tên database không hợp lệ: '{databaseName}'.", nameof(databaseName));
    }

    private Task RunPsqlAsync(params string[] args) => RunPsqlInAsync("smashcourt_test", args);

    private async Task RunPsqlInAsync(string database, params string[] args)
    {
        var command = new List<string> { "psql", "-U", "test", "-d", database, "-v", "ON_ERROR_STOP=1", "-q" };
        command.AddRange(args);

        var result = await _container.ExecAsync(command);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"psql {string.Join(' ', args)} thất bại (exit {result.ExitCode}): {result.Stderr}");
    }

    /// <summary>Đường dẫn bản sao schema trong thư mục output của test.</summary>
    public static string SchemaFilePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Database", "schema_smashcourt.sql");
}
