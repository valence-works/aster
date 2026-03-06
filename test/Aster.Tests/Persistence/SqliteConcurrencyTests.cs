using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;

namespace Aster.Tests.Persistence;

/// <summary>
/// Tests for concurrent save/activate conflict detection via the Sqlite provider.
/// Verifies unbroken version history and typed ConcurrencyConflict outcome.
/// </summary>
public sealed class SqliteConcurrencyTests : IDisposable
{
    private readonly SqliteTestFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    private static Resource MakeResource(string resourceId = "res-1", int version = 1)
    {
        return new Resource
        {
            ResourceId = resourceId,
            Id = Guid.NewGuid().ToString(),
            DefinitionId = "Product",
            Version = version,
            Created = DateTime.UtcNow,
        };
    }

    [Fact]
    public async Task SaveVersionAsync_DuplicateNonV1Version_ThrowsTypedConcurrencyException()
    {
        var store = fixture.WriteStore;

        await store.SaveVersionAsync(MakeResource(version: 1));
        await store.SaveVersionAsync(MakeResource(version: 2));

        // Attempt to insert the same (ResourceId, Version=2) again
        var ex = await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.SaveVersionAsync(MakeResource(version: 2)).AsTask());

        Assert.Contains("Version 2", ex.Message);
        Assert.Contains("res-1", ex.Message);
    }

    [Fact]
    public async Task SaveVersionAsync_SequentialVersions_PreservesHistory()
    {
        var store = fixture.WriteStore;

        // Insert 5 versions sequentially
        for (var v = 1; v <= 5; v++)
            await store.SaveVersionAsync(MakeResource(version: v));

        var versions = (await store.GetVersionsAsync("res-1")).ToList();

        Assert.Equal(5, versions.Count);
        for (var i = 0; i < 5; i++)
            Assert.Equal(i + 1, versions[i].Version);
    }

    [Fact]
    public async Task SaveVersionAsync_ConcurrentV1Inserts_OnlyOneSucceeds()
    {
        var store = fixture.WriteStore;

        var tasks = Enumerable.Range(0, 5).Select(_ =>
            store.SaveVersionAsync(MakeResource(resourceId: "race-res", version: 1)).AsTask());

        var results = await Task.WhenAll(tasks.Select(async t =>
        {
            try
            {
                await t;
                return true; // success
            }
            catch (ConcurrencyException)
            {
                return false; // expected conflict
            }
            catch (DuplicateResourceIdException)
            {
                return false; // expected duplicate
            }
        }));

        // Exactly one should succeed, the rest should fail
        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task SaveVersionAsync_UniqueVersionId_Enforced()
    {
        var store = fixture.WriteStore;

        var versionId = Guid.NewGuid().ToString();
        var v1 = new Resource
        {
            ResourceId = "res-1",
            Id = versionId,
            DefinitionId = "Product",
            Version = 1,
            Created = DateTime.UtcNow,
        };
        await store.SaveVersionAsync(v1);

        // Try to insert V2 with the same VersionId (Id)
        var v2 = new Resource
        {
            ResourceId = "res-1",
            Id = versionId, // duplicate VersionId
            DefinitionId = "Product",
            Version = 2,
            Created = DateTime.UtcNow,
        };

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.SaveVersionAsync(v2).AsTask());
    }
}
