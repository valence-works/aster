using Aster.Core.Abstractions;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonPolicyDefinitionTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-policy-{Guid.NewGuid():N}.db");

    public void Dispose() => PolicyTestFixtures.DeleteSqliteFiles(databasePath);

    [Fact]
    public async Task RegisterDefinitionAsync_PreservesPolicyDeclarationsAcrossProviders()
    {
        await using (var provider = PolicyTestFixtures.CreateSqliteProvider(databasePath))
        {
            var store = provider.GetRequiredService<IResourceDefinitionStore>();
            await store.RegisterDefinitionAsync(PolicyTestFixtures.ProductDefinition(PolicyTestFixtures.ArchivePolicy()));
        }

        await using var secondProvider = PolicyTestFixtures.CreateSqliteProvider(databasePath);
        var secondStore = secondProvider.GetRequiredService<IResourceDefinitionStore>();

        var definition = await secondStore.GetDefinitionAsync("Product");

        Assert.NotNull(definition);
        var policy = Assert.Single(definition.PolicyDeclarations);
        Assert.Equal("archive-old", policy.PolicyId);
    }
}
