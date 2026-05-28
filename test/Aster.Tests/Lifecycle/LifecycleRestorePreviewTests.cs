using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Lifecycle;

public sealed class LifecycleRestorePreviewTests : IDisposable
{
    private readonly ServiceProvider provider = LifecycleRestoreTestFixtures.CreateCoreProvider();

    public void Dispose() => provider.Dispose();

    [Fact]
    public async Task PreviewRestoreAsync_ReturnsRestorableAlreadyRestoredSkippedAndEmptyResults()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "unmarked");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var preview = await restore.PreviewRestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("unmarked", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
            ],
        });
        var empty = await restore.PreviewRestoreAsync(new ResourceLifecycleRestoreRequest());

        Assert.Equal(1, preview.RestorableCount);
        Assert.Equal(1, preview.AlreadyRestoredCount);
        Assert.Equal(1, preview.SkippedCount);
        Assert.Empty(empty.Candidates);
    }

    [Fact]
    public async Task PreviewRestoreAsync_ReturnsMarkerMismatchAndMissingTargetDiagnostics()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var preview = await restore.PreviewRestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.SoftDeleted),
                LifecycleRestoreTestFixtures.Candidate("missing", ResourceLifecycleMarkerState.Archived),
            ],
        });

        Assert.Collection(
            preview.Candidates,
            first => AssertDiagnostic(first, ResourcePolicyDiagnosticCodes.LifecycleRestoreMarkerMismatch),
            second => AssertDiagnostic(second, ResourcePolicyDiagnosticCodes.LifecycleMarkerTargetNotFound));
    }

    [Fact]
    public async Task PreviewRestoreAsync_DoesNotClearMarkersForValidInvalidOrUnsupportedCandidates()
    {
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "archived");
        await LifecycleRestoreTestFixtures.SaveProductAsync(provider, "unsupported");
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "archived", ResourceLifecycleMarkerState.Archived);
        await LifecycleRestoreTestFixtures.MarkAsync(provider, "unsupported", ResourceLifecycleMarkerState.Archived);
        var restore = provider.GetRequiredService<IResourceLifecycleRestoreService>();

        var preview = await restore.PreviewRestoreAsync(new ResourceLifecycleRestoreRequest
        {
            Candidates =
            [
                LifecycleRestoreTestFixtures.Candidate("archived", ResourceLifecycleMarkerState.Archived),
                LifecycleRestoreTestFixtures.Candidate("unsupported", ResourceLifecycleMarkerState.None),
                LifecycleRestoreTestFixtures.Candidate(null, ResourceLifecycleMarkerState.Archived),
            ],
        });

        Assert.Equal(1, preview.RestorableCount);
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "archived"));
        Assert.NotNull(await LifecycleRestoreTestFixtures.ReadMarkerAsync(provider, "unsupported"));
    }

    private static void AssertDiagnostic(ResourceLifecycleRestoreCandidateResult result, string code)
    {
        Assert.Equal(ResourceLifecycleRestoreCandidateStatus.Failed, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }
}
