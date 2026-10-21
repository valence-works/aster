using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Tests.Persistence;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aster.Tests.PostgresJsonb;

/// <summary>
/// #97: concurrent writers against one Postgres database. Each writer has its own provider, as separate
/// processes would.
/// </summary>
public sealed class PostgresJsonbConcurrencyTests : IDisposable
{
    private const int Writers = 8;

    private readonly PostgresTestDatabase database = new();

    public void Dispose() => database.Dispose();

    [SkippableFact]
    public async Task UpdateAsync_ConcurrentWritersOnOneBaseVersion_OneWinsTheRestGetConcurrencyException()
    {
        await using (var setup = database.CreateProvider())
        {
            await TenantScopeTestFixtures.RegisterProductDefinitionAsync(setup);
            await TenantScopeTestFixtures.CreateProductAsync(setup, "contended", "v1");
        }

        var providers = Enumerable.Range(0, Writers).Select(_ => database.CreateProvider()).ToList();
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = providers.Select((provider, index) => Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    await provider.GetRequiredService<IResourceManager>().UpdateAsync("contended", new UpdateResourceRequest
                    {
                        BaseVersion = 1,
                        AspectUpdates = { ["Title"] = new { Title = $"writer {index}" } },
                    });
                    return (Exception?)null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            })).ToList();
            start.SetResult();
            var outcomes = await Task.WhenAll(running);

            Assert.Single(outcomes, static outcome => outcome is null);
            Assert.All(outcomes.Where(static outcome => outcome is not null), static outcome => Assert.IsType<ConcurrencyException>(outcome));

            var versions = await providers[0].GetRequiredService<IResourceManager>().GetVersionsAsync("contended");
            Assert.Equal([1, 2], versions.Select(static version => version.Version));
        }
        finally
        {
            foreach (var provider in providers)
                await provider.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task RegisterDefinitionAsync_ConcurrentRegistrations_GetDistinctConsecutiveVersions()
    {
        var versions = await RunConcurrentlyAsync(async provider =>
        {
            var store = provider.GetRequiredService<IResourceDefinitionStore>();
            await store.RegisterDefinitionAsync(new ResourceDefinitionBuilder().WithDefinitionId("Contended").Build());
        }, async provider =>
        {
            // Probe one past the expected range, so a lost or duplicated version shows up as a gap or an extra.
            var store = provider.GetRequiredService<IResourceDefinitionStore>();
            var found = new List<int>();
            for (var version = 1; version <= Writers + 1; version++)
            {
                if (await store.GetDefinitionVersionAsync("Contended", version) is not null)
                    found.Add(version);
            }

            return found;
        });

        Assert.Equal(Enumerable.Range(1, Writers), versions);
    }

    [SkippableFact]
    public async Task AspectRegisterAsync_ConcurrentRegistrations_GetDistinctConsecutiveVersions()
    {
        var versions = await RunConcurrentlyAsync(async provider =>
        {
            await provider.GetRequiredService<IAspectDefinitionStore>().RegisterAsync(new AspectDefinition
            {
                AspectDefinitionId = "Contended",
                Id = Guid.NewGuid().ToString("N"),
                Version = 0,
            });
        }, async provider =>
            (await provider.GetRequiredService<IAspectDefinitionStore>().ListVersionsAsync("Contended")).Select(static aspect => aspect.Version).ToList());

        Assert.Equal(Enumerable.Range(1, Writers), versions);
    }

    [SkippableFact]
    public async Task PruneVersionAsync_WaitsForAnActivationOfTheSameResource_AndSeesIt()
    {
        await using var provider = database.CreateProvider();
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        foreach (var version in new[] { 1, 2, 3 })
            await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("pruned", $"v{version}", version: version));

        // Another session holds the resource's write lock (as UpdateActivationAsync does) and activates v1.
        await using var activation = new NpgsqlConnection(database.ConnectionString);
        await activation.OpenAsync();
        await using var transaction = await activation.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('resource_versions/default/pruned', 0));", activation, transaction))
            await lockCommand.ExecuteNonQueryAsync();

        var prune = provider.GetRequiredService<IResourceVersionPruningStore>()
            .PruneVersionAsync("pruned", 1, TenantScope.Default)
            .AsTask();
        await Task.Delay(500);
        Assert.False(prune.IsCompleted, "Prune ran while another session held the resource's write lock.");

        await using (var activate = new NpgsqlCommand(
            "INSERT INTO activation_states (tenant_id, resource_id, channel, payload) VALUES ('default', 'pruned', 'Published', @payload::jsonb);",
            activation,
            transaction))
        {
            activate.Parameters.AddWithValue("payload", """{"tenantScope":{"tenantId":"default"},"resourceId":"pruned","channel":"Published","activeVersions":[1]}""");
            await activate.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => prune);
        Assert.Contains("active", exception.Message);
    }

    [SkippableFact]
    public async Task ActivateAndPruneOfOneVersion_Concurrently_EndActiveOrPrunedNeverBoth()
    {
        // #180: the manager checks that a version exists before activating it; the store re-checks under the lock
        // prune takes, so the race ends with exactly one of the two operations applied.
        const int Resources = 20;
        await using var setup = database.CreateProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(setup);
        var writer = setup.GetRequiredService<IResourceVersionWriter>();
        for (var index = 0; index < Resources; index++)
        {
            foreach (var version in new[] { 1, 2 })
                await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource($"raced-{index}", $"v{version}", version: version));
        }

        await using var activating = database.CreateProvider();
        await using var pruning = database.CreateProvider();
        var manager = activating.GetRequiredService<IResourceManager>();
        var pruner = pruning.GetRequiredService<IResourceVersionPruningStore>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var races = Enumerable.Range(0, Resources).Select(index => (
            Activate: Task.Run(async () =>
            {
                await start.Task;
                await manager.ActivateAsync($"raced-{index}", 1, "Published");
            }),
            Prune: Task.Run(async () =>
            {
                await start.Task;
                return await pruner.PruneVersionAsync($"raced-{index}", 1, TenantScope.Default);
            }))).ToList();
        start.SetResult();

        var reader = setup.GetRequiredService<IResourceVersionReader>();
        foreach (var (index, race) in races.Select((race, index) => (index, race)))
        {
            var activated = await Succeeded(race.Activate);
            var pruned = await Succeeded(race.Prune);
            var versions = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                Scope = ResourceVersionScope.AllVersions,
                ResourceIds = [$"raced-{index}"],
            })).Select(static resource => resource.Version).ToList();
            var active = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
            {
                Scope = ResourceVersionScope.Active,
                ActivationChannel = "Published",
                ResourceIds = [$"raced-{index}"],
            })).Select(static resource => resource.Version).ToList();

            Assert.True(activated != pruned, $"raced-{index}: activated {activated}, pruned {pruned}.");
            Assert.Equal(activated ? [1, 2] : [2], versions);
            Assert.Equal(activated ? [1] : [], active);
        }

        static async Task<bool> Succeeded(Task task)
        {
            try
            {
                await task;
                return task is not Task<bool> pruned || pruned.Result;
            }
            catch (Exception exception) when (exception is VersionNotFoundException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    [SkippableFact]
    public async Task SchemaBootstrap_ConcurrentProvidersOnAFreshDatabase_AllStart()
    {
        // Every provider bootstraps the schema when its stores are first resolved.
        var providers = Enumerable.Range(0, Writers).Select(_ => database.CreateProvider()).ToList();
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = providers.Select(provider => Task.Run(async () =>
            {
                await start.Task;
                _ = provider.GetRequiredService<IResourceDefinitionStore>();
                _ = provider.GetRequiredService<IAspectDefinitionStore>();
            })).ToList();
            start.SetResult();
            await Task.WhenAll(running);
        }
        finally
        {
            foreach (var provider in providers)
                await provider.DisposeAsync();
        }

        Assert.Equal(
            ["activation_states", "aspect_definitions", "aster_infrastructure_steps", "lifecycle_markers", "resource_definitions", "resource_versions"],
            ReadTableNames());
    }

    [SkippableFact]
    public async Task SchemaBootstrap_RepeatedOnAPopulatedDatabase_KeepsData()
    {
        await using (var first = database.CreateProvider())
        {
            await TenantScopeTestFixtures.RegisterProductDefinitionAsync(first);
            await TenantScopeTestFixtures.CreateProductAsync(first, "kept", "Kept");
        }

        await using var second = database.CreateProvider();
        var resource = await second.GetRequiredService<IResourceManager>().GetLatestVersionAsync("kept");

        Assert.NotNull(resource);
        Assert.Equal("kept", resource.ResourceId);
    }

    private async Task<List<int>> RunConcurrentlyAsync(Func<ServiceProvider, Task> write, Func<ServiceProvider, Task<List<int>>> read)
    {
        var providers = Enumerable.Range(0, Writers).Select(_ => database.CreateProvider()).ToList();
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = providers.Select(provider => Task.Run(async () =>
            {
                await start.Task;
                await write(provider);
            })).ToList();
            start.SetResult();
            await Task.WhenAll(running);

            return (await read(providers[0])).Order().ToList();
        }
        finally
        {
            foreach (var provider in providers)
                await provider.DisposeAsync();
        }
    }

    private List<string> ReadTableNames()
    {
        using var connection = new NpgsqlConnection(database.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;",
            connection);
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(0));

        return names;
    }
}
