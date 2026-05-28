using Aster.Core.Abstractions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleRestoreDiagnosticsTests : IDisposable
{
    private readonly ServiceProvider provider = LifecycleRestoreTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task RestoreAsync_InvalidAndUnsupportedCandidatesFailWithoutClearingMarkers()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "missing-state");
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "unsupported-state");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "missing-state", ResourceLifecycleMarkerState.Archived);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "unsupported-state", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                null!,
                LifecycleRestoreTestFixtures.Candidate(null, ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("missing-state", null),
                LifecycleRestoreTestFixtures.Candidate("unsupported-state", (ResourceLifecycleMarkerState)999),
            ],
        });

        Assert.All(result.Candidates, candidate => Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Failed, candidate.Status));
        Assert.Contains(result.Candidates[0].Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid);
        Assert.Contains(result.Candidates[1].Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid);
        Assert.Contains(result.Candidates[2].Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid);
        Assert.Contains(result.Candidates[3].Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreStateUnsupported);
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "missing-state"));
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "unsupported-state"));
    }

    [Fact]
    public async Task PreviewRestoreAsync_NullCandidateFailsWithDiagnostic()
    {
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var result = await restore.PreviewRestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [null!],
        });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Failed, candidate.Status);
        Assert.Null(candidate.ResourceId);
        Assert.Null(candidate.ExpectedState);
        Assert.Contains(candidate.Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid);
    }

    [Fact]
    public void AddAsterCore_WhenActiveMarkerStoreCannotClearMarkersFailsFastForRestore()
    {
        using var customProvider = new ServiceCollection()
            .AddAsterCore()
            .AddSingleton<IResourceLifecycleMarkerStore, MarkerStoreWithoutClear>()
            .BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => customProvider.GetRequiredService<IResourceLifecycleRestoreService>());

        Assert.Contains(nameof(IResourceLifecycleMarkerStore), exception.Message);
        Assert.Contains(nameof(IResourceLifecycleMarkerClearStore), exception.Message);
    }

    [Fact]
    public async Task RestoreAsync_MissingTargetMismatchAndStalePreviewFailClosed()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();
        var store = provider.GetRequiredService<IResourceLifecycleMarkerStore>();

        var preview = await restore.PreviewRestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived)],
        });
        await store.SaveMarkerAsync(new ResourceLifecycleMarker
        {
            TenantScope = TenantScope.Default,
            ResourceId = "archived",
            State = ResourceLifecycleMarkerState.SoftDeleted,
            MarkedAt = DateTimeOffset.UtcNow,
        });

        var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                LifecycleRestoreTestFixtures.Candidate("missing", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
            ],
        });

        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Restorable, Assert.Single(preview.Candidates).Status);
        Assert.Collection(
            result.Candidates,
            first => Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound),
            second => Assert.Contains(second.Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreMarkerMismatch));
        var current = await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "archived");
        Assert.Equal(ResourceLifecycleMarkerState.SoftDeleted, current?.State);
    }

    [Fact]
    public async Task RestoreAsync_WhenMarkerChangesBeforeClearDoesNotClearNewState()
    {
        var store = new ChangingMarkerClearStore();
        var restore = new ResourceLifecycleRestoreService(new SingleResourceVersionReader(), store);

        var result = await restore.RestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates = [LifecycleRestoreTestFixtures.Candidate("product-1", ResourceLifecycleMarkerState.Archived)],
        });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Failed, candidate.Status);
        Assert.Contains(candidate.Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreMarkerMismatch);
        Assert.Equal(ResourceLifecycleMarkerState.SoftDeleted, store.Current.State);
    }

    private sealed class MarkerStoreWithoutClear : IResourceLifecycleMarkerStore
    {
        public ValueTask<ResourceLifecycleMarker?> GetMarkerAsync(
            string resourceId,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ResourceLifecycleMarker?>(null);

        public ValueTask<IReadOnlyDictionary<string, ResourceLifecycleMarker>> GetMarkersAsync(
            IEnumerable<string> resourceIds,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, ResourceLifecycleMarker>>(
                new Dictionary<string, ResourceLifecycleMarker>(StringComparer.Ordinal));

        public ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(
            ResourceLifecycleMarker marker,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(marker);
    }

    private sealed class SingleResourceVersionReader : IResourceVersionReader
    {
        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
            ResourceVersionReadRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IEnumerable<Resource>>(
            [
                new Resource
                {
                    ResourceId = "product-1",
                    Id = "product-1-1",
                    DefinitionId = "Product",
                    Version = 1,
                    Created = DateTime.UtcNow,
                },
            ]);
    }

    private sealed class ChangingMarkerClearStore : IResourceLifecycleMarkerClearStore
    {
        private ResourceLifecycleMarker current = Marker(ResourceLifecycleMarkerState.Archived);

        public ResourceLifecycleMarker Current => current;

        public ValueTask<ResourceLifecycleMarker?> GetMarkerAsync(
            string resourceId,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ResourceLifecycleMarker?>(current);

        public ValueTask<IReadOnlyDictionary<string, ResourceLifecycleMarker>> GetMarkersAsync(
            IEnumerable<string> resourceIds,
            TenantScope tenantScope,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, ResourceLifecycleMarker>>(
                new Dictionary<string, ResourceLifecycleMarker>(StringComparer.Ordinal)
                {
                    ["product-1"] = current,
                });

        public ValueTask<ResourceLifecycleMarker> SaveMarkerAsync(
            ResourceLifecycleMarker marker,
            CancellationToken cancellationToken = default)
        {
            current = marker;
            return ValueTask.FromResult(marker);
        }

        public ValueTask<bool> ClearMarkerAsync(
            string resourceId,
            TenantScope tenantScope,
            ResourceLifecycleMarkerState expectedState,
            CancellationToken cancellationToken = default)
        {
            current = Marker(ResourceLifecycleMarkerState.SoftDeleted);
            return ValueTask.FromResult(false);
        }

        private static ResourceLifecycleMarker Marker(ResourceLifecycleMarkerState state) =>
            new()
            {
                TenantScope = TenantScope.Default,
                ResourceId = "product-1",
                State = state,
                MarkedAt = DateTimeOffset.UtcNow,
            };
    }
}
