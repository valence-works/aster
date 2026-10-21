using Aster.Core.Abstractions;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

public abstract class PolicyDefinitionStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task RegisterDefinitionAsync_PreservesPolicyDeclarationsAcrossProviders()
    {
        await using (var provider = Database.CreateProvider())
        {
            var store = provider.GetRequiredService<IResourceDefinitionStore>();
            await store.RegisterDefinitionAsync(PolicyTestFixtures.ProductDefinition(PolicyTestFixtures.ArchivePolicy()));
        }

        await using var secondProvider = Database.CreateProvider();
        var secondStore = secondProvider.GetRequiredService<IResourceDefinitionStore>();

        var definition = await secondStore.GetDefinitionAsync("Product");

        Assert.NotNull(definition);
        var policy = Assert.Single(definition.PolicyDeclarations);
        Assert.Equal("archive-old", policy.PolicyId);
    }
}

public sealed class SqliteJsonPolicyDefinitionTests() : PolicyDefinitionStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbPolicyDefinitionTests() : PolicyDefinitionStoreTests(new PostgresTestDatabase());

public sealed class SqlServerJsonPolicyDefinitionTests() : PolicyDefinitionStoreTests(new SqlServerTestDatabase());
