using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class TenantPolicyApplicationTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_UsesEffectiveTenantScopeForLookupsAndMarkerWrites()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA, PolicyTestFixtures.ArchivePolicy());
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB, PolicyTestFixtures.ArchivePolicy());
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", tenantScope: TenantScopeTestFixtures.TenantA);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", tenantScope: TenantScopeTestFixtures.TenantB);

        var result = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = [PolicyTestFixtures.ApplicationCandidate("shared")],
        });

        Assert.Equal(1, result.AppliedCount);
        var markers = provider.GetRequiredService<IResourceLifecycleMarkerStore>();
        Assert.Equal(ResourceLifecycleMarkerState.Archived, (await markers.GetMarkerAsync("shared", TenantScopeTestFixtures.TenantA))!.State);
        Assert.Null(await markers.GetMarkerAsync("shared", TenantScopeTestFixtures.TenantB));
    }

    [Fact]
    public async Task ApplyAsync_OutsideTenantResourceFailsAsTargetNotFoundWithoutMarkerWrite()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA, PolicyTestFixtures.ArchivePolicy());
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB, PolicyTestFixtures.ArchivePolicy());
        await PolicyTestFixtures.SaveResourceAsync(provider, "tenant-b-only", tenantScope: TenantScopeTestFixtures.TenantB);

        var result = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            AppliedAt = DateTimeOffset.UtcNow,
            Candidates = [PolicyTestFixtures.ApplicationCandidate("tenant-b-only")],
        });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourcePolicyApplicationCandidateStatus.Failed, candidate.Status);
        Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound, candidate.Diagnostics.Single().Code);
        Assert.Null(await provider.GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync("tenant-b-only", TenantScopeTestFixtures.TenantA));
    }
}
