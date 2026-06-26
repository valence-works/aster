using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class LifecycleMarkerTransitionTests : IDisposable
{
    private static readonly DateTimeOffset MarkedAt = new(2026, 6, 26, 9, 0, 0, TimeSpan.Zero);
    private readonly ServiceProvider provider = PolicyTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task ApplyAsync_AppliesMarkerWhenTargetExists()
    {
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var transition = provider.GetRequiredService<IResourceLifecycleMarkerTransitionService>();

        var result = await transition.ApplyAsync(ApplyRequest("product-1", ResourceLifecycleMarkerState.Archived));

        Assert.Equal(ResourceLifecycleMarkerTransitionStatus.Applied, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, result.Marker!.State);
        Assert.Equal("product-1", result.Marker.ResourceId);
    }

    [Fact]
    public async Task ApplyAsync_ReapplyingSameStateIsAlreadySatisfied()
    {
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var transition = provider.GetRequiredService<IResourceLifecycleMarkerTransitionService>();

        var first = await transition.ApplyAsync(ApplyRequest("product-1", ResourceLifecycleMarkerState.Archived));
        var second = await transition.ApplyAsync(ApplyRequest("product-1", ResourceLifecycleMarkerState.Archived));

        Assert.Equal(ResourceLifecycleMarkerTransitionStatus.Applied, first.Status);
        Assert.Equal(ResourceLifecycleMarkerTransitionStatus.AlreadySatisfied, second.Status);
        Assert.Empty(second.Diagnostics);
        Assert.Equal(first.Marker, second.Marker);
    }

    [Fact]
    public async Task ApplyAsync_RejectsUnsupportedState()
    {
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var transition = provider.GetRequiredService<IResourceLifecycleMarkerTransitionService>();

        var result = await transition.ApplyAsync(ApplyRequest("product-1", ResourceLifecycleMarkerState.None));

        Assert.Equal(ResourceLifecycleMarkerTransitionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.PolicyInvalid, diagnostic.Code);
        Assert.Null(result.Marker);
    }

    [Fact]
    public async Task ApplyAsync_RejectsMissingTarget()
    {
        var transition = provider.GetRequiredService<IResourceLifecycleMarkerTransitionService>();

        var result = await transition.ApplyAsync(ApplyRequest("missing", ResourceLifecycleMarkerState.Archived));

        Assert.Equal(ResourceLifecycleMarkerTransitionStatus.Failed, result.Status);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound, diagnostic.Code);
        Assert.Null(result.Marker);
    }

    [Fact]
    public async Task ApplyAsync_RejectsConflictingExistingMarker()
    {
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var transition = provider.GetRequiredService<IResourceLifecycleMarkerTransitionService>();
        var archived = await transition.ApplyAsync(ApplyRequest("product-1", ResourceLifecycleMarkerState.Archived));

        var result = await transition.ApplyAsync(ApplyRequest("product-1", ResourceLifecycleMarkerState.SoftDeleted));

        Assert.Equal(ResourceLifecycleMarkerTransitionStatus.Failed, result.Status);
        Assert.Equal(archived.Marker, result.Marker);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourcePolicyDiagnosticCodes.LifecycleMarkerConflict, diagnostic.Code);
    }

    [Fact]
    public async Task ApplyAsync_UsesPreloadedCurrentMarker()
    {
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var transition = provider.GetRequiredService<IResourceLifecycleMarkerTransitionService>();
        var existing = new ResourceLifecycleMarker
        {
            TenantScope = TenantScope.Default,
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = MarkedAt,
        };

        var result = await transition.ApplyAsync(ApplyRequest(
            "product-1",
            ResourceLifecycleMarkerState.Archived,
            currentMarker: existing));

        Assert.Equal(ResourceLifecycleMarkerTransitionStatus.AlreadySatisfied, result.Status);
        Assert.Equal(existing, result.Marker);
        var persisted = await provider.GetRequiredService<IResourceLifecycleMarkerStore>()
            .GetMarkerAsync("product-1", TenantScope.Default);
        Assert.Null(persisted);
    }

    private static ResourceLifecycleMarkerTransitionApplyRequest ApplyRequest(
        string resourceId,
        ResourceLifecycleMarkerState state,
        ResourceLifecycleMarker? currentMarker = null) =>
        new()
        {
            TenantScope = TenantScope.Default,
            ResourceId = resourceId,
            State = state,
            MarkedAt = MarkedAt,
            HasCurrentMarker = currentMarker is not null,
            CurrentMarker = currentMarker,
        };
}
