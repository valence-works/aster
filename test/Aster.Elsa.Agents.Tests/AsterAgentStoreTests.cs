using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Agents.Persistence.Filters;
using Microsoft.Extensions.DependencyInjection;
using AgentConfigAspect = Aster.Elsa.Agents.Aspects.AgentConfig;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// Elsa's <see cref="IAgentStore"/> contract on the Aster store, on the in-memory and SQLite providers: what Elsa writes
/// it reads back, filters behave as in Elsa's own stores, and deletes, references and facets work as documented.
/// </summary>
public sealed class AsterAgentStoreTests
{
    public static TheoryData<string> Providers => AgentTestHost.AllProviders;

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task An_added_agent_reads_back_with_every_field(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var writer = TestAgents.Create("writer", "Writer");

        await host.WithStoreAsync(store => store.AddAsync(writer));

        var read = await host.WithStoreAsync(store => store.GetAsync("writer"));
        Assert.Equal(TestAgents.Snapshot(writer), TestAgents.Snapshot(read));
        Assert.IsType<AsterAgentStore>(await host.WithStoreAsync(store => Task.FromResult(store)));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Null_fields_round_trip_as_null(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("bare", "Bare");
        agent.Description = null!;
        agent.AgentConfig.FunctionName = null;
        agent.AgentConfig.PromptTemplate = null!;
        agent.AgentConfig.Skills = null!;
        agent.AgentConfig.Agents = [];

        await host.WithStoreAsync(store => store.AddAsync(agent));

        Assert.Equal(TestAgents.Snapshot(agent), TestAgents.Snapshot(await host.WithStoreAsync(store => store.GetAsync("bare"))));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task An_agent_without_an_id_gets_one(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("", "Anonymous");

        await host.WithStoreAsync(store => store.AddAsync(agent));

        Assert.False(string.IsNullOrEmpty(agent.Id));
        Assert.Equal("Anonymous", (await host.WithStoreAsync(store => store.GetAsync(agent.Id)))!.Name);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Adding_an_existing_id_throws_and_keeps_the_agent(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        await host.WithStoreAsync(store => store.AddAsync(TestAgents.Create("writer", "Writer")));

        await Assert.ThrowsAsync<DuplicateResourceIdException>(() => host.WithStoreAsync(store => store.AddAsync(TestAgents.Create("writer", "Other"))));

        Assert.Equal("Writer", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.Name);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Every_update_saves_a_new_version_and_reads_return_the_latest(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");
        await host.WithStoreAsync(store => store.AddAsync(agent));

        agent.AgentConfig.PromptTemplate = "Version two";
        await host.WithStoreAsync(store => store.UpdateAsync(agent));
        agent.AgentConfig.PromptTemplate = "Version three";
        await host.WithStoreAsync(store => store.UpdateAsync(agent));

        Assert.Equal("Version three", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.AgentConfig.PromptTemplate);
        var manager = host.Services.GetRequiredService<IResourceManager>();
        Assert.Equal([1, 2, 3], (await manager.GetVersionsAsync("writer")).Select(static version => version.Version).Order());
        Assert.Equal([3], (await manager.GetActiveVersionsAsync("writer", AsterAgentStoreOptions.DefaultChannel)).Select(static version => version.Version));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Updating_an_unknown_agent_adds_it(string provider)
    {
        await using var host = AgentTestHost.Start(provider);

        await host.WithStoreAsync(store => store.UpdateAsync(TestAgents.Create("writer", "Writer")));

        Assert.Equal("Writer", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.Name);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Saved_versions_do_not_change_when_the_entity_does(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");
        await host.WithStoreAsync(store => store.AddAsync(agent));

        agent.AgentConfig.InputVariables.Single().Description = "Changed after saving";
        agent.AgentConfig.ExecutionSettings.MaxTokens = 1;

        var read = await host.WithStoreAsync(store => store.GetAsync("writer"));
        Assert.Equal("The topic", read!.AgentConfig.InputVariables.Single().Description);
        Assert.Equal(500, read.AgentConfig.ExecutionSettings.MaxTokens);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Filters_match_as_in_Elsas_stores(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        await host.WithStoreAsync(async store =>
        {
            await store.AddAsync(TestAgents.Create("a", "Alpha"));
            await store.AddAsync(TestAgents.Create("b", "Beta"));
            await store.AddAsync(TestAgents.Create("c", "Gamma"));
        });

        await host.WithStoreAsync(async store =>
        {
            Assert.Equal(["a", "b", "c"], (await store.ListAsync()).Select(static agent => agent.Id));
            Assert.Equal("b", (await store.FindAsync(new AgentDefinitionFilter { Name = "Beta" }))!.Id);
            Assert.Null(await store.FindAsync(new AgentDefinitionFilter { Name = "beta" }));
            Assert.Null(await store.GetAsync("A"));
            Assert.Null(await store.FindAsync(new AgentDefinitionFilter { Name = "Beta", NotId = "b" }));
            Assert.Equal("c", (await store.FindAsync(new AgentDefinitionFilter { Ids = ["c", "missing"] }))!.Id);
            Assert.Equal("a", (await store.FindAsync(new AgentDefinitionFilter { NotId = "b" }))!.Id);
            Assert.Equal("a", (await store.FindAsync(new AgentDefinitionFilter()))!.Id);
            Assert.Null(await store.GetAsync(""));
        });
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Deleting_soft_deletes_and_keeps_the_history(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");
        await host.WithStoreAsync(store => store.AddAsync(agent));

        await host.WithStoreAsync(store => store.DeleteAsync(agent));
        await host.WithStoreAsync(store => store.DeleteAsync(agent));
        await host.WithStoreAsync(store => store.DeleteAsync(TestAgents.Create("missing", "Missing")));

        Assert.Null(await host.WithStoreAsync(store => store.GetAsync("writer")));
        Assert.Empty(await host.WithStoreAsync(store => store.ListAsync()));
        Assert.Equal(ResourceLifecycleMarkerState.SoftDeleted, (await host.Services.GetRequiredService<IResourceLifecycleMarkerService>().GetAsync("writer"))!.State);
        Assert.Single(await host.Services.GetRequiredService<IResourceManager>().GetVersionsAsync("writer"));

        // Aster's restore service brings the agent back exactly as it was.
        var restore = await host.Services.GetRequiredService<IResourceLifecycleRestoreService>().RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [new ResourceLifecycleRestoreCandidate { ResourceId = "writer", ExpectedState = ResourceLifecycleMarkerState.SoftDeleted }],
        });
        Assert.Equal(1, restore.RestoredCount);
        Assert.Equal(TestAgents.Snapshot(agent), TestAgents.Snapshot(await host.WithStoreAsync(store => store.GetAsync("writer"))));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Updating_a_deleted_agent_throws_and_keeps_it_deleted(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");
        await host.WithStoreAsync(store => store.AddAsync(agent));
        await host.WithStoreAsync(store => store.DeleteAsync(agent));

        // Someone still editing the agent saves: the edit must not vanish into a version nobody can read.
        agent.Description = "Edited after the delete.";
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.WithStoreAsync(store => store.UpdateAsync(agent)));

        Assert.Single(await host.Services.GetRequiredService<IResourceManager>().GetVersionsAsync("writer"));
        Assert.Null(await host.WithStoreAsync(store => store.GetAsync("writer")));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task An_agent_that_references_a_deleted_agent_saves_as_it_reads(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var researcher = TestAgents.Create("researcher-id", "Researcher");
        await host.WithStoreAsync(async store =>
        {
            await store.AddAsync(researcher);
            await store.AddAsync(TestAgents.Create("writer-id", "Writer", "Researcher"));
            await store.DeleteAsync(researcher);
        });

        // The reference still reads as the deleted agent's name, so an unrelated edit sends that name back.
        var writer = (await host.WithStoreAsync(store => store.GetAsync("writer-id")))!;
        Assert.Equal(["Researcher"], writer.AgentConfig.Agents);
        writer.AgentConfig.PromptTemplate = "Write it shorter.";
        await host.WithStoreAsync(store => store.UpdateAsync(writer));

        var stored = await host.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("writer-id");
        var config = stored!.GetAspect<AgentConfigAspect>(AsterAgentResource.ConfigAspectKey, host.Services.GetRequiredService<ITypedAspectBinder>());
        Assert.Equal(2, stored.Version);
        Assert.Equal(["researcher-id"], config!.Agents!.AsEnumerable());
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Delete_many_deletes_the_matching_agents(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        await host.WithStoreAsync(async store =>
        {
            await store.AddAsync(TestAgents.Create("a", "Alpha"));
            await store.AddAsync(TestAgents.Create("b", "Beta"));
            await store.AddAsync(TestAgents.Create("c", "Gamma"));
        });

        Assert.Equal(2, await host.WithStoreAsync(store => store.DeleteManyAsync(new AgentDefinitionFilter { Ids = ["a", "c"] })));

        Assert.Equal(["b"], (await host.WithStoreAsync(store => store.ListAsync())).Select(static agent => agent.Id));
        Assert.Equal(1, await host.WithStoreAsync(store => store.DeleteManyAsync(new AgentDefinitionFilter())));
        Assert.Empty(await host.WithStoreAsync(store => store.ListAsync()));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Agent_references_are_stored_as_resource_ids_and_read_as_names(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var researcher = TestAgents.Create("researcher-id", "Researcher");
        await host.WithStoreAsync(async store =>
        {
            await store.AddAsync(researcher);
            await store.AddAsync(TestAgents.Create("writer-id", "Writer", "Researcher"));
        });

        var stored = await host.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("writer-id");
        var config = stored!.GetAspect<AgentConfigAspect>(AsterAgentResource.ConfigAspectKey, host.Services.GetRequiredService<ITypedAspectBinder>());
        Assert.Equal(["researcher-id"], config!.Agents!.AsEnumerable());
        Assert.Equal(["Researcher"], (await host.WithStoreAsync(store => store.GetAsync("writer-id")))!.AgentConfig.Agents);

        // The reference follows the agent, not the name: after a rename, it reads as the new name.
        researcher.Name = "Analyst";
        await host.WithStoreAsync(store => store.UpdateAsync(researcher));
        Assert.Equal(["Analyst"], (await host.WithStoreAsync(store => store.GetAsync("writer-id")))!.AgentConfig.Agents);
        Assert.Contains(await host.WithStoreAsync(store => store.ListAsync()), agent => agent.Id == "writer-id" && agent.AgentConfig.Agents.SequenceEqual(["Analyst"]));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Reverse_reference_lookup_finds_the_agents_that_use_an_agent(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        await host.WithStoreAsync(async store =>
        {
            await store.AddAsync(TestAgents.Create("researcher-id", "Researcher"));
            await store.AddAsync(TestAgents.Create("writer-id", "Writer", "Researcher"));
            await store.AddAsync(TestAgents.Create("editor-id", "Editor", "Researcher", "Writer"));
        });

        var referrers = await host.Services.GetRequiredService<IResourceReferenceService>().GetReferrersAsync("researcher-id");

        Assert.Equal(["editor-id", "writer-id"], referrers.Items.Select(static referrer => referrer.Resource.ResourceId));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task A_reference_to_an_unknown_agent_is_rejected_under_strict_validation(string provider)
    {
        await using var host = AgentTestHost.Start(provider);

        var exception = await Assert.ThrowsAsync<FacetValueValidationException>(() => host.WithStoreAsync(store => store.AddAsync(TestAgents.Create("writer", "Writer", "Nobody"))));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(FacetValueDiagnosticCodes.ReferenceTargetMissing, diagnostic.Code);
        Assert.Equal((AsterAgentResource.ConfigAspectKey, "Agents", 0), (diagnostic.AspectKey, diagnostic.FacetDefinitionId, diagnostic.ElementIndex));
        Assert.Contains("Nobody", diagnostic.Message);
        Assert.Null(await host.WithStoreAsync(store => store.GetAsync("writer")));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task A_reference_to_an_unknown_agent_is_kept_as_written_under_lenient_validation(string provider)
    {
        await using var host = AgentTestHost.Start(provider, aster: static options => options.ReferenceValidation = ReferenceValidationMode.Lenient);

        await host.WithStoreAsync(store => store.AddAsync(TestAgents.Create("writer", "Writer", "Nobody")));

        Assert.Equal(["Nobody"], (await host.WithStoreAsync(store => store.GetAsync("writer")))!.AgentConfig.Agents);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Facets_make_agents_queryable_through_Aster(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var plain = TestAgents.Create("plain", "Plain");
        plain.AgentConfig.Skills = ["DocumentQuery"];
        await host.WithStoreAsync(async store =>
        {
            await store.AddAsync(TestAgents.Create("painter", "Painter"));
            await store.AddAsync(plain);
        });

        var withImages = await host.Services.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery
        {
            DefinitionId = AsterAgentResource.DefinitionId,
            Filter = new FacetValueFilter(AsterAgentResource.ConfigAspectKey, "Skills", "ImageGenerator", ComparisonOperator.ArrayContains),
        });

        Assert.Equal(["painter"], withImages.Select(static resource => resource.ResourceId));
    }

    [Fact]
    public async Task An_agent_without_a_config_is_refused()
    {
        await using var host = AgentTestHost.Start();
        var agent = TestAgents.Create("writer", "Writer");
        agent.AgentConfig = null!;

        await Assert.ThrowsAsync<ArgumentException>(() => host.WithStoreAsync(store => store.AddAsync(agent)));
    }

    [Fact]
    public async Task The_definition_is_registered_once_per_tenant_on_first_write()
    {
        await using var host = AgentTestHost.Start(AgentTestHost.Sqlite);
        var definitions = host.Services.GetRequiredService<IResourceDefinitionStore>();
        Assert.Null(await definitions.GetDefinitionAsync(AsterAgentResource.DefinitionId));

        await host.WithStoreAsync(async store =>
        {
            await store.AddAsync(TestAgents.Create("a", "Alpha"));
            await store.AddAsync(TestAgents.Create("b", "Beta"));
        });

        var definition = await definitions.GetDefinitionAsync(AsterAgentResource.DefinitionId);
        Assert.Equal(1, definition!.Version);
        Assert.Null(await definitions.GetDefinitionAsync(AsterAgentResource.DefinitionId, TenantScope.FromTenantId("acme"), CancellationToken.None));

        // Another process on the same database finds the definition and doesn't register it again.
        await using var second = AgentTestHost.Start(AgentTestHost.Sqlite, database: host.Database);
        await second.WithStoreAsync(store => store.AddAsync(TestAgents.Create("c", "Gamma")));
        Assert.Equal(1, (await definitions.GetDefinitionAsync(AsterAgentResource.DefinitionId))!.Version);
    }

    [Fact]
    public void Invalid_options_are_refused()
    {
        var services = new ServiceCollection().AddAsterElsaAgents(static options => options.Channel = " production").BuildServiceProvider();

        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AsterAgentStoreOptions>>().Value);
    }
}
