using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
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
                LifecycleRestoreTestFixtures.Candidate(null, ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("missing-state", null),
                LifecycleRestoreTestFixtures.Candidate("unsupported-state", (ResourceLifecycleMarkerState)999),
            ],
        });

        Assert.All(result.Candidates, candidate => Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Failed, candidate.Status));
        Assert.Contains(result.Candidates[0].Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid);
        Assert.Contains(result.Candidates[1].Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreCandidateInvalid);
        Assert.Contains(result.Candidates[2].Diagnostics, diagnostic => diagnostic.Code == ResourcePolicyDiagnosticCodes.LifecycleRestoreStateUnsupported);
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "missing-state"));
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "unsupported-state"));
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
}
