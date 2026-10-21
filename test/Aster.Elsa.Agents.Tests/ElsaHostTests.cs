using Aster.Core.Abstractions;
using Elsa.Agents;
using Elsa.Agents.Activities.ActivityProviders;
using Elsa.Agents.Persistence;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// The Aster store behind Elsa's own seam (<c>AgentPersistenceFeature.UseAgentStore</c>) in an in-process Elsa host:
/// Elsa's agent manager writes through it, and Elsa builds its kernel configuration and agent activities from it.
/// </summary>
public sealed class ElsaHostTests
{
    public static TheoryData<string> Providers => new() { AgentTestHost.InMemory, AgentTestHost.Sqlite };

    [Fact]
    public async Task The_Aster_store_is_used_only_when_selected()
    {
        await using var withAster = AgentTestHost.Start();
        await using var withoutAster = AgentTestHost.Start(useAsterStore: false);

        Assert.IsType<AsterAgentStore>(await withAster.WithStoreAsync(Task.FromResult));
        Assert.IsType<MemoryAgentStore>(await withoutAster.WithStoreAsync(Task.FromResult));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Elsas_agent_manager_and_kernel_config_run_on_Aster(string provider)
    {
        await using var host = AgentTestHost.Start(provider);

        await host.InTenantAsync(null, async scope =>
        {
            var manager = scope.GetRequiredService<IAgentManager>();
            await manager.AddAsync(TestAgents.Create("researcher", "Researcher"));
            await manager.AddAsync(TestAgents.Create("writer", "Writer", "Researcher"));

            Assert.False(await manager.IsNameUniqueAsync("Writer", null));
            Assert.True(await manager.IsNameUniqueAsync("Writer", "writer"));
            Assert.Equal("Agent 1", await manager.GenerateUniqueNameAsync());

            var kernel = await scope.GetRequiredService<IKernelConfigProvider>().GetKernelConfigAsync();
            Assert.Equal(["Researcher", "Writer"], kernel.Agents.Keys.Order());
            Assert.Equal(["Researcher"], kernel.Agents["Writer"].Agents);
        });

        // The writes are Aster resource versions.
        Assert.Equal(1, (await host.Services.GetRequiredService<IResourceManager>().GetLatestVersionAsync("writer"))!.Version);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Elsas_agent_activities_follow_the_version_active_in_the_channel(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        var agent = TestAgents.Create("writer", "Writer");

        await host.InTenantAsync(null, async scope =>
        {
            var manager = scope.GetRequiredService<IAgentManager>();
            await manager.AddAsync(agent);
            agent.AgentConfig.FunctionName = "Copywriter";
            agent.AgentConfig.Description = "Writes copy";
            await manager.UpdateAsync(agent);

            Assert.Equal("Writes copy", await DescribeAsync(scope, "Copywriter"));

            // Rolling back in Aster is what Elsa then builds the activity from.
            await scope.GetRequiredService<IResourceManager>().ActivateAsync("writer", 1, AsterAgentStoreOptions.DefaultChannel);
            Assert.Null(await DescribeAsync(scope, "Copywriter"));
            Assert.Equal("Configuration of Writer", await DescribeAsync(scope, "WriterFunction"));

            // Deleting through Elsa removes the activity. (Elsa's registry keeps a provider's old descriptors when the
            // provider returns none at all, so another agent stays.)
            await manager.AddAsync(TestAgents.Create("researcher", "Researcher"));
            await manager.DeleteAsync(agent);
            Assert.Null(await DescribeAsync(scope, "WriterFunction"));
            Assert.Equal("Configuration of Researcher", await DescribeAsync(scope, "ResearcherFunction"));
        });
    }

    /// <summary>Refreshes Elsa's activity registry from the agent activity provider and describes one agent activity.</summary>
    private static async Task<string?> DescribeAsync(IServiceProvider scope, string activityName)
    {
        var registry = scope.GetRequiredService<IActivityRegistry>();
        await registry.RefreshDescriptorsAsync(scope.GetRequiredService<AgentActivityProvider>());
        return registry.Find(descriptor => descriptor.Category == "Agents" && descriptor.Name == activityName)?.Description;
    }
}
