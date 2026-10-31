using SmashCourt_BE.Tests.Integration.Http;
using Xunit;

namespace SmashCourt_BE.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgreSqlIntegrationFixture>, ICollectionFixture<HttpTestHostFixture>
{
    public const string Name = "PostgreSQL integration tests";
}
