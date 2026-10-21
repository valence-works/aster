using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Common.Multitenancy;
using Microsoft.Extensions.DependencyInjection;
using TenantScope = Aster.Core.Models.Tenancy.TenantScope;

namespace Aster.Elsa.Agents.Tests;

/// <summary>
/// Every store call runs in the Aster tenant mapped from Elsa's current tenant, so one host keeps each tenant's agents
/// apart; the agnostic tenant is refused.
/// </summary>
public sealed class AgentTenancyTests
{
    public static TheoryData<string> Providers => AgentTestHost.AllProviders;

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task Each_Elsa_tenant_sees_only_its_own_agents(string provider)
    {
        await using var host = AgentTestHost.Start(provider);

        await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAgentStore>().AddAsync(TestAgents.Create("writer", "Acme Writer")));
        await host.InTenantAsync("globex", scope => scope.GetRequiredService<IAgentStore>().AddAsync(TestAgents.Create("writer", "Globex Writer")));

        Assert.Equal("Acme Writer", (await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAgentStore>().GetAsync("writer")))!.Name);
        Assert.Equal("Globex Writer", (await host.InTenantAsync("globex", scope => scope.GetRequiredService<IAgentStore>().GetAsync("writer")))!.Name);
        Assert.Empty(await host.WithStoreAsync(store => store.ListAsync()));

        var acme = await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAgentStore>().ListAsync());
        Assert.Equal("acme", Assert.Single(acme).TenantId);

        var manager = host.Services.GetRequiredService<IResourceManager>();
        Assert.NotNull(await manager.GetLatestVersionAsync("writer", TenantScope.FromTenantId("acme"), CancellationToken.None));
        Assert.Null(await manager.GetLatestVersionAsync("writer"));
    }

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public async Task References_resolve_within_the_tenant(string provider)
    {
        await using var host = AgentTestHost.Start(provider);
        await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAgentStore>().AddAsync(TestAgents.Create("researcher", "Researcher")));

        // Globex has no Researcher, so the reference fails strict validation there.
        await Assert.ThrowsAsync<FacetValueValidationException>(() =>
            host.InTenantAsync("globex", scope => scope.GetRequiredService<IAgentStore>().AddAsync(TestAgents.Create("writer", "Writer", "Researcher"))));

        await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAgentStore>().AddAsync(TestAgents.Create("writer", "Writer", "Researcher")));
        Assert.Equal(["Researcher"], (await host.InTenantAsync("acme", scope => scope.GetRequiredService<IAgentStore>().GetAsync("writer")))!.AgentConfig.Agents);
    }

    [Fact]
    public async Task The_agnostic_tenant_is_refused()
    {
        await using var host = AgentTestHost.Start();

        var exception = await Assert.ThrowsAsync<TenantScopeException>(() =>
            host.InTenantAsync(Tenant.AgnosticTenantId, scope => scope.GetRequiredService<IAgentStore>().ListAsync()));
        Assert.Equal(TenantScopeException.InvalidCode, exception.Code);

        await Assert.ThrowsAsync<TenantScopeException>(() =>
            host.InTenantAsync(Tenant.AgnosticTenantId, scope => scope.GetRequiredService<IAgentStore>().AddAsync(TestAgents.Create("writer", "Writer"))));
    }
}
