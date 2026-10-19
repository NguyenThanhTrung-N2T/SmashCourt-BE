using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;

namespace SmashCourt_BE.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private IServiceScope? _scope;
    private IDbContextTransaction? _transaction;

    protected IntegrationTestBase(PostgreSqlIntegrationFixture fixture)
    {
        Fixture = fixture;
    }

    protected PostgreSqlIntegrationFixture Fixture { get; }
    protected SmashCourtContext DbContext { get; private set; } = null!;
    protected TestDataSeeder Seeder { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _scope = Fixture.CreateScope();
        DbContext = _scope.ServiceProvider.GetRequiredService<SmashCourtContext>();
        Seeder = new TestDataSeeder(DbContext);
        _transaction = await DbContext.Database.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
        }

        _scope?.Dispose();
    }
}
