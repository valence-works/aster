using Aster.Core.Abstractions;
using Aster.Core.Models.Tenancy;
using Elsa.Agents;
using Elsa.Agents.Persistence;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Agents.Persistence.EFCore;
using Elsa.Agents.Persistence.Entities;
using Elsa.Common.Services;
using Elsa.Persistence.EFCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// The one-time import from Elsa's own agent stores (in-memory and EF Core) into the Aster store: every agent arrives
/// with its ID and fields, references resolve whatever the order, and running it again changes nothing.
/// </summary>
public sealed class AgentImportTests
{
    public static TheoryData<string> Providers => new() { AgentTestHost.InMemory, AgentTestHost.Sqlite };

    /// <summary>
    /// Agents whose IDs sort before the agents they reference, a two-agent cycle and an agent that references itself.
    /// </summary>
    private static List<AgentDefinition> Catalog() =>
    [
        TestAgents.Create("a-editor", "Editor", "Writer", "Researcher"),
        TestAgents.Create("b-writer", "Writer", "Researcher"),
        TestAgents.Create("c-researcher", "Researcher"),
        TestAgents.Create("d-ping", "Ping", "Pong"),
        TestAgents.Create("e-pong", "Pong", "Ping"),
        TestAgents.Create("f-narcissus", "Narcissus", "Narcissus"),
    ];

    private static async Task<MemoryAgentStore> SeedMemoryStoreAsync(IEnumerable<AgentDefinition> agents)
    {
        var store = new MemoryAgentStore(new MemoryStore<AgentDefinition>());
        foreach (var agent in agents)
            await store.AddAsync(agent);
        return store;
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Importing_from_Elsas_memory_store_copies_every_agent(string provider)
    {
        var source = await SeedMemoryStoreAsync(Catalog());
        await using var host = AgentTestHost.Start(provider);

        var result = await host.InTenantAsync(null, scope => scope.GetRequiredService<IAsterAgentImporter>().ImportAsync(source));

        Assert.True(result.Succeeded, string.Join("; ", result.Items.Select(static item => item.Error)));
        Assert.Equal(6, result.ImportedCount);
        Assert.Equal(
            (await source.ListAsync()).OrderBy(static agent => agent.Id).Select(TestAgents.Snapshot),
            (await host.WithStoreAsync(store => store.ListAsync())).Select(TestAgents.Snapshot));

        // Agents in a reference cycle take two versions; the others one.
        var manager = host.Services.GetRequiredService<IResourceManager>();
        foreach (var (id, versions) in new[] { ("a-editor", 1), ("b-writer", 1), ("c-researcher", 1), ("d-ping", 2), ("e-pong", 2), ("f-narcissus", 2) })
            Assert.Equal(versions, (await manager.GetLatestVersionAsync(id))!.Version);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Importing_again_changes_nothing_and_adds_only_new_agents(string provider)
    {
        var catalog = Catalog();
        var source = await SeedMemoryStoreAsync(catalog);
        await using var host = AgentTestHost.Start(provider);
        var importer = (IServiceProvider scope) => scope.GetRequiredService<IAsterAgentImporter>();
        await host.InTenantAsync(null, scope => importer(scope).ImportAsync(source));
        var before = (await host.WithStoreAsync(store => store.ListAsync())).Select(TestAgents.Snapshot).ToList();

        var again = await host.InTenantAsync(null, scope => importer(scope).ImportAsync(source));

        Assert.Equal(6, again.SkippedCount);
        Assert.Equal(0, again.ImportedCount);
        Assert.Equal(before, (await host.WithStoreAsync(store => store.ListAsync())).Select(TestAgents.Snapshot));
        Assert.Equal(1, (await host.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("a-editor"))!.Version);

        // A later run imports what is new, and leaves agents already in Aster as Aster has them.
        catalog[0].Description = "Changed in Elsa after the import";
        await source.UpdateAsync(catalog[0]);
        await source.AddAsync(TestAgents.Create("g-critic", "Critic", "Editor"));

        var third = await host.InTenantAsync(null, scope => importer(scope).ImportAsync(source));

        Assert.Equal([("g-critic", AsterAgentImportStatus.Imported)], third.Items.Where(static item => item.Status != AsterAgentImportStatus.Skipped).Select(static item => (item.AgentId, item.Status)));
        Assert.Equal("The Editor agent", (await host.WithStoreAsync(store => store.GetAsync("a-editor")))!.Description);
        Assert.Equal(["Editor"], (await host.WithStoreAsync(store => store.GetAsync("g-critic")))!.AgentConfig.Agents);
    }

    [Fact]
    public async Task A_failing_agent_is_reported_and_the_others_are_imported()
    {
        var source = await SeedMemoryStoreAsync([TestAgents.Create("researcher", "Researcher"), TestAgents.Create("writer", "Writer", "Researcher", "Nobody")]);
        await using var host = AgentTestHost.Start();

        var result = await host.InTenantAsync(null, scope => scope.GetRequiredService<IAsterAgentImporter>().ImportAsync(source));

        Assert.False(result.Succeeded);
        Assert.Equal(AsterAgentImportStatus.Imported, result.Items.Single(static item => item.AgentId == "researcher").Status);
        var failed = result.Items.Single(static item => item.AgentId == "writer");
        Assert.Equal(AsterAgentImportStatus.Failed, failed.Status);
        Assert.Contains("Nobody", failed.Error);
        Assert.Null(await host.WithStoreAsync(store => store.GetAsync("writer")));
    }

    [Fact]
    public async Task Imported_agents_are_active_even_without_auto_activation()
    {
        var source = await SeedMemoryStoreAsync([TestAgents.Create("writer", "Writer")]);
        await using var host = AgentTestHost.Start(configure: static options => options.AutoActivate = false);

        await host.InTenantAsync(null, scope => scope.GetRequiredService<IAsterAgentImporter>().ImportAsync(source));

        Assert.Equal("Writer", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.Name);
    }

    [Fact]
    public async Task The_import_runs_in_the_current_Elsa_tenant()
    {
        var source = await SeedMemoryStoreAsync([TestAgents.Create("writer", "Writer")]);
        await using var host = AgentTestHost.Start();

        await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAsterAgentImporter>().ImportAsync(source));

        Assert.NotNull(await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAgentStore>().GetAsync("writer")));
        Assert.Null(await host.WithStoreAsync(store => store.GetAsync("writer")));
        Assert.NotNull(await host.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("writer", TenantScope.FromTenantId("acme"), CancellationToken.None));
    }

    [Fact]
    public async Task Importing_from_the_Aster_store_itself_is_refused()
    {
        await using var host = AgentTestHost.Start();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            host.InTenantAsync(null, scope => scope.GetRequiredService<IAsterAgentImporter>().ImportAsync(scope.GetRequiredService<IAgentStore>())));
    }

    [Fact]
    public async Task Importing_from_Elsas_EF_Core_store_moves_a_live_Elsa_database_onto_Aster()
    {
        // Before: Elsa keeps agents in its EF Core store, in the same SQLite database Aster will use.
        var database = AgentTestHost.NewDatabase();
        try
        {
            AsterAgentImportResult result;
            List<string> elsaAgents;
            await using (var before = AgentTestHost.Start(
                             AgentTestHost.Sqlite,
                             useAsterStore: false,
                             persistence: feature =>
                             {
                                 feature.UseEntityFrameworkCore(ef => ef.UseSqlite(AgentTestHost.ConnectionString(database)));
                                 feature.Services.AddAsterElsaAgents();
                             },
                             database: database))
            {
                await using (var context = await before.Services.GetRequiredService<IDbContextFactory<AgentsDbContext>>().CreateDbContextAsync())
                    await context.Database.MigrateAsync();

                await before.InTenantAsync(null, async scope =>
                {
                    var manager = scope.GetRequiredService<IAgentManager>();
                    foreach (var agent in Catalog())
                        await manager.AddAsync(agent);
                });

                Assert.IsType<EFCoreAgentStore>(await before.WithStoreAsync(Task.FromResult));
                elsaAgents = (await before.WithStoreAsync(store => store.ListAsync())).OrderBy(static agent => agent.Id).Select(TestAgents.Snapshot).ToList();

                // The migration step: read Elsa's store, write Aster.
                result = await before.InTenantAsync(null, scope => scope.GetRequiredService<IAsterAgentImporter>().ImportAsync(scope.GetRequiredService<IAgentStore>()));
                var again = await before.InTenantAsync(null, scope => scope.GetRequiredService<IAsterAgentImporter>().ImportAsync(scope.GetRequiredService<IAgentStore>()));
                Assert.Equal(6, again.SkippedCount);
            }

            Assert.Equal(6, result.ImportedCount);

            // After: the same database, with Elsa on the Aster store.
            await using var after = AgentTestHost.Start(AgentTestHost.Sqlite, database: database);
            Assert.Equal(elsaAgents, (await after.WithStoreAsync(store => store.ListAsync())).Select(TestAgents.Snapshot));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }
}
