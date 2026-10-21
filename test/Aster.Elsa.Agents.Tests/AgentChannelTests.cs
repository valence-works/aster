using Aster.Core.Abstractions;
using Aster.Core.Models.Portability;
using Elsa.Agents.Persistence.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// The store reads the version active in its channel: drafts stay invisible until activated, activating an older
/// version rolls back, and a store on another channel sees another version of the same agent.
/// </summary>
public sealed class AgentChannelTests
{
    public static TheoryData<string> Providers => AgentTestHost.AllProviders;

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Without_auto_activation_saves_are_drafts_until_promoted(string provider)
    {
        await using var host = AgentTestHost.Start(provider, static options => options.AutoActivate = false);
        var manager = host.Services.GetRequiredService<IResourceManager>();
        var agent = TestAgents.Create("writer", "Writer");

        await host.WithStoreAsync(store => store.AddAsync(agent));
        Assert.Null(await host.WithStoreAsync(store => store.GetAsync("writer")));
        Assert.Empty(await host.WithStoreAsync(store => store.ListAsync()));

        await manager.ActivateAsync("writer", 1, AsterAgentStoreOptions.DefaultChannel);
        Assert.Equal("Writer", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.Name);

        // An update is a draft: Elsa keeps running version 1 until version 2 is activated.
        agent.AgentConfig.PromptTemplate = "Reviewed prompt";
        await host.WithStoreAsync(store => store.UpdateAsync(agent));
        Assert.NotEqual("Reviewed prompt", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.AgentConfig.PromptTemplate);

        await manager.ActivateAsync("writer", 2, AsterAgentStoreOptions.DefaultChannel);
        Assert.Equal("Reviewed prompt", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.AgentConfig.PromptTemplate);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Activating_an_older_version_rolls_back(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");
        await host.WithStoreAsync(store => store.AddAsync(agent));
        var original = agent.AgentConfig.PromptTemplate;
        agent.AgentConfig.PromptTemplate = "A prompt that went wrong";
        await host.WithStoreAsync(store => store.UpdateAsync(agent));

        await host.Services.GetRequiredService<IResourceManager>().ActivateAsync("writer", 1, AsterAgentStoreOptions.DefaultChannel);

        Assert.Equal(original, (await host.WithStoreAsync(store => store.GetAsync("writer")))!.AgentConfig.PromptTemplate);
        Assert.Equal(original, (await host.WithStoreAsync(store => store.ListAsync())).Single().AgentConfig.PromptTemplate);
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Stores_on_different_channels_see_the_version_active_in_their_own(string provider)
    {
        await using var host = AgentTestHost.Start(provider, static options => options.Channel = "staging");
        var agent = TestAgents.Create("writer", "Writer");

        await host.InTenantAsync(null, async scope =>
        {
            var staging = scope.GetRequiredService<IAgentStore>();
            var production = host.CreateStore(scope, new AsterAgentStoreOptions { Channel = "production", AutoActivate = false });

            await staging.AddAsync(agent);
            Assert.NotNull(await staging.GetAsync("writer"));
            Assert.Null(await production.GetAsync("writer"));

            // Promotion is an Aster activation: production now runs what staging tested.
            await scope.GetRequiredService<IResourceManager>().ActivateAsync("writer", 1, "production");
            agent.AgentConfig.PromptTemplate = "Next prompt";
            await staging.UpdateAsync(agent);

            Assert.Equal("Next prompt", (await staging.GetAsync("writer"))!.AgentConfig.PromptTemplate);
            Assert.NotEqual("Next prompt", (await production.GetAsync("writer"))!.AgentConfig.PromptTemplate);
        });
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task With_several_active_versions_the_highest_is_read(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");
        await host.WithStoreAsync(store => store.AddAsync(agent));
        agent.AgentConfig.PromptTemplate = "Variant B";
        await host.WithStoreAsync(store => store.UpdateAsync(agent));

        await host.Services.GetRequiredService<IResourceManager>().ActivateAsync("writer", 1, AsterAgentStoreOptions.DefaultChannel, allowMultipleActive: true);

        Assert.Equal("Variant B", (await host.WithStoreAsync(store => store.GetAsync("writer")))!.AgentConfig.PromptTemplate);
        Assert.Single(await host.WithStoreAsync(store => store.ListAsync()));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Agents_export_with_their_history_and_activations(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");
        await host.WithStoreAsync(store => store.AddAsync(TestAgents.Create("researcher", "Researcher")));
        agent.AgentConfig.Agents = ["Researcher"];
        await host.WithStoreAsync(store => store.AddAsync(agent));
        await host.WithStoreAsync(store => store.UpdateAsync(agent));

        var export = await host.Services.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["writer"],
            IncludeReferencedResources = true,
        });

        var snapshot = export.Snapshot!;
        Assert.Equal([AsterAgentResource.DefinitionId], snapshot.Definitions.Select(static definition => definition.DefinitionId).Distinct());
        Assert.Equal(["researcher:1", "writer:1", "writer:2"], snapshot.Resources.Select(static version => $"{version.ResourceId}:{version.Version}").Order());
        Assert.Contains(snapshot.ActivationStates, static state => state.ResourceId == "writer" && state.ActiveVersions.SequenceEqual([2]));
    }
}
