using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.SqliteJson;

public sealed class SqliteJsonPolicyApplicationTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-policy-application-{Guid.NewGuid():N}.db");

    public void Dispose() => PolicyTestFixtures.DeleteSqliteFiles(databasePath);

    [Fact]
    public async Task ApplyAsync_UsesSqliteJsonProviderStoresThroughCoreRegistration()
    {
        await using (var provider = PolicyTestFixtures.CreateSqliteProvider(databasePath))
        {
            await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, policies: [PolicyTestFixtures.ArchivePolicy()]);
            await PolicyTestFixtures.SaveResourceAsync(provider, "archived");

            var result = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
            {
                AppliedAt = DateTimeOffset.UtcNow,
                Candidates = [PolicyTestFixtures.ApplicationCandidate("archived")],
            });

            Assert.Equal(1, result.AppliedCount);
        }

        await using var secondProvider = PolicyTestFixtures.CreateSqliteProvider(databasePath);
        var results = (await secondProvider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();

        var resource = Assert.Single(results);
        Assert.Equal("archived", resource.ResourceId);
    }
}
