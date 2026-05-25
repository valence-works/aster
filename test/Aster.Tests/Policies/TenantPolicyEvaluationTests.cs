using Aster.Core.Abstractions;
using Aster.Core.Models.Policies;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class TenantPolicyEvaluationTests : IDisposable
{
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task PreviewAsync_ReturnsOnlyCandidatesWithinTenant()
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA, PolicyTestFixtures.ArchivePolicy());
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB, PolicyTestFixtures.ArchivePolicy());
        await PolicyTestFixtures.SaveResourceAsync(provider, "tenant-a-product", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), tenantScope: TenantScopeTestFixtures.TenantA);
        await PolicyTestFixtures.SaveResourceAsync(provider, "tenant-b-product", created: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), tenantScope: TenantScopeTestFixtures.TenantB);
        var evaluation = provider.GetRequiredService<IResourcePolicyEvaluationService>();

        var preview = await evaluation.PreviewAsync(new ResourcePolicyEvaluationRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            EvaluationTimestamp = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero),
        });

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("tenant-a-product", candidate.ResourceId);
    }
}
