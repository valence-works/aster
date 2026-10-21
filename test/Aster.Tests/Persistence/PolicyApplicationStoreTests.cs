using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Tests.Policies;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

public abstract class PolicyApplicationStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task ApplyAsync_UsesSqliteJsonProviderStoresThroughCoreRegistration()
    {
        await using (var provider = Database.CreateProvider())
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

        await using var secondProvider = Database.CreateProvider();
        var results = (await secondProvider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery
        {
            LifecycleState = ResourceLifecycleMarkerState.Archived,
        })).ToList();

        var resource = Assert.Single(results);
        Assert.Equal("archived", resource.ResourceId);
    }
}

public sealed class SqliteJsonPolicyApplicationTests() : PolicyApplicationStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbPolicyApplicationTests() : PolicyApplicationStoreTests(new PostgresTestDatabase());

public sealed class SqlServerJsonPolicyApplicationTests() : PolicyApplicationStoreTests(new SqlServerTestDatabase());
