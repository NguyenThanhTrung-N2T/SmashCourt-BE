using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmashCourt_BE.Data;
using Testcontainers.PostgreSql;

namespace SmashCourt_BE.Tests.Integration;

public sealed class PostgreSqlIntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine")
        .WithDatabase("smashcourt_test")
        .WithUsername("test")
        .WithPassword("test123")
        .Build();

    private ServiceProvider? _serviceProvider;

    public IServiceScope CreateScope()
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("The PostgreSQL fixture has not been initialized.");

        return _serviceProvider.CreateScope();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<SmashCourtContext>(options =>
            options.UseNpgsql(_container.GetConnectionString()));

        _serviceProvider = services.BuildServiceProvider();
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SmashCourtContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider != null)
            await _serviceProvider.DisposeAsync();

        await _container.DisposeAsync();
    }
}
