using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

public sealed class LifecycleMarkerTransitionProviderParityTests : IDisposable
{
    private static readonly DateTimeOffset TransitionedAt = new(2026, 6, 26, 10, 0, 0, TimeSpan.Zero);
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-marker-transition-parity-{Guid.NewGuid():N}.db");

    public void Dispose() => PolicyTestFixtures.DeleteSqliteFiles(databasePath);

    [Fact]
    public async Task BuiltInProviders_ObserveSameLifecycleMarkerTransitionBehavior()
    {
        await using var inMemory = PolicyTestFixtures.CreateCoreProvider();
        await using var sqlite = PolicyTestFixtures.CreateSqliteProvider(databasePath);

        var inMemorySnapshot = await CaptureTransitionSnapshotAsync(inMemory);
        var sqliteSnapshot = await CaptureTransitionSnapshotAsync(sqlite);
        var expected = new ProviderTransitionSnapshot(
            ManualResult: "True:manual-archived:Archived",
            PolicyStatuses: "policy-archived:Applied:Archived,policy-soft-deleted:Applied:SoftDeleted",
            RestorePreviewStatuses: "restore-target:Restorable:Archived",
            RestoreStatuses: "restore-target:Restored:Archived",
            ArchivedQueryIds: "manual-archived|policy-archived",
            SoftDeletedQueryIds: "policy-soft-deleted",
            UnmarkedQueryIds: "restore-target|unmarked",
            HistoryStates: "manual-archived[1:Archived:]|policy-archived[1:Archived:]|policy-soft-deleted[1:SoftDeleted:]|restore-target[1:None:|2:None:Published]|unmarked[1:None:]",
            ExportedMarkers: "manual-archived:Archived|policy-archived:Archived|policy-soft-deleted:SoftDeleted",
            ExportedResourceVersions: "manual-archived:1:manual-archived-1|policy-archived:1:policy-archived-1|policy-soft-deleted:1:policy-soft-deleted-1|restore-target:1:restore-target-1|restore-target:2:restore-target-2|unmarked:1:unmarked-1",
            VersionsUnchanged: true,
            ActivationUnchanged: true);

        Assert.Equal(expected, inMemorySnapshot);
        Assert.Equal(expected, sqliteSnapshot);
        Assert.Equal(inMemorySnapshot, sqliteSnapshot);
    }

    private static async Task<ProviderTransitionSnapshot> CaptureTransitionSnapshotAsync(IServiceProvider provider)
    {
        await SeedAsync(provider);
        var versionsBefore = await ReadAllVersionsAsync(provider);
        var activeBefore = await ReadPublishedVersionsAsync(provider, "restore-target");

        var markerService = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        var manual = await markerService.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "manual-archived",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = TransitionedAt,
            Reason = "manual parity",
        });

        var policy = await provider.GetRequiredService<IResourcePolicyApplicationService>().ApplyAsync(new ResourcePolicyApplicationRequest
        {
            AppliedAt = TransitionedAt,
            Reason = "policy parity",
            Candidates =
            [
                PolicyTestFixtures.ApplicationCandidate("policy-archived", "archive"),
                PolicyTestFixtures.ApplicationCandidate(
                    "policy-soft-deleted",
                    "soft-delete",
                    ResourcePolicyKind.SoftDelete,
                    ResourcePolicyOutcome.SoftDelete),
            ],
        });

        await markerService.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "restore-target",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = TransitionedAt,
            Reason = "restore parity",
        });

        var restoreService = provider.GetRequiredService<IResourceLifecycleRestoreService>();
        var restoreRequest = new ResourceLifecycleRestoreRequest
        {
            Candidates = [RestoreCandidate("restore-target", ResourceLifecycleMarkerState.Archived)],
            RestoredAt = TransitionedAt,
        };
        var restorePreview = await restoreService.PreviewRestoreAsync(restoreRequest);
        var restore = await restoreService.RestoreAsync(restoreRequest);

        var versionsAfter = await ReadAllVersionsAsync(provider);
        var activeAfter = await ReadPublishedVersionsAsync(provider, "restore-target");
        Assert.Equal(versionsBefore, versionsAfter);
        Assert.Equal(activeBefore, activeAfter);

        var archivedIds = await QueryResourceIdsAsync(provider, ResourceLifecycleMarkerState.Archived);
        var softDeletedIds = await QueryResourceIdsAsync(provider, ResourceLifecycleMarkerState.SoftDeleted);
        var unmarkedIds = await QueryResourceIdsAsync(provider, ResourceLifecycleMarkerState.None);
        var histories = await ReadHistoryStatesAsync(provider);
        var exported = await ExportMarkerSnapshotAsync(provider);

        return new ProviderTransitionSnapshot(
            ManualResult: $"{manual.Succeeded}:{manual.Marker?.ResourceId}:{manual.Marker?.State}",
            PolicyStatuses: string.Join(",", policy.Candidates.Select(static candidate => $"{candidate.ResourceId}:{candidate.Status}:{candidate.Marker?.State}")),
            RestorePreviewStatuses: string.Join(",", restorePreview.Candidates.Select(static candidate => $"{candidate.ResourceId}:{candidate.Status}:{candidate.Marker?.State}")),
            RestoreStatuses: string.Join(",", restore.Candidates.Select(static candidate => $"{candidate.ResourceId}:{candidate.Status}:{candidate.Marker?.State}")),
            ArchivedQueryIds: archivedIds,
            SoftDeletedQueryIds: softDeletedIds,
            UnmarkedQueryIds: unmarkedIds,
            HistoryStates: histories,
            ExportedMarkers: exported.Markers,
            ExportedResourceVersions: exported.ResourceVersions,
            VersionsUnchanged: versionsBefore == versionsAfter,
            ActivationUnchanged: activeBefore == activeAfter);
    }

    private static async Task SeedAsync(IServiceProvider provider)
    {
        await PolicyTestFixtures.RegisterProductDefinitionAsync(
            provider,
            policies:
            [
                PolicyTestFixtures.ArchivePolicy("archive"),
                PolicyTestFixtures.SoftDeletePolicy("soft-delete"),
            ]);
        await PolicyTestFixtures.SaveResourceAsync(provider, "manual-archived");
        await PolicyTestFixtures.SaveResourceAsync(provider, "policy-archived");
        await PolicyTestFixtures.SaveResourceAsync(provider, "policy-soft-deleted");
        await PolicyTestFixtures.SaveResourceAsync(provider, "restore-target");
        await PolicyTestFixtures.SaveResourceAsync(provider, "restore-target", version: 2);
        await PolicyTestFixtures.SaveResourceAsync(provider, "unmarked");
        await PolicyTestFixtures.ActivateAsync(provider, "restore-target", version: 2);
    }

    private static ResourceLifecycleRestoreCandidate RestoreCandidate(
        string resourceId,
        ResourceLifecycleMarkerState expectedState) =>
        new()
        {
            ResourceId = resourceId,
            ExpectedState = expectedState,
        };

    private static async Task<string> QueryResourceIdsAsync(IServiceProvider provider, ResourceLifecycleMarkerState state)
    {
        var resources = await provider.GetRequiredService<IResourceQueryService>().QueryAsync(new ResourceQuery
        {
            LifecycleState = state,
            Sorts = [new SortExpression("ResourceId")],
        });

        return Join(resources.Select(static resource => resource.ResourceId));
    }

    private static async Task<string> ReadHistoryStatesAsync(IServiceProvider provider)
    {
        var result = await provider.GetRequiredService<IResourceVersionHistoryService>().GetHistoriesAsync(
            new ResourceVersionHistoryBatchRequest
            {
                ResourceIds = ["manual-archived", "policy-archived", "policy-soft-deleted", "restore-target", "unmarked"],
            });

        return Join(result.Histories.Select(static history =>
            $"{history.ResourceId}[{Join(history.Versions.Select(static version => $"{version.Version}:{version.LifecycleState}:{Join(version.ActiveChannels)}"))}]"));
    }

    private static async Task<ExportSnapshot> ExportMarkerSnapshotAsync(IServiceProvider provider)
    {
        var result = await provider.GetRequiredService<IResourcePortabilityService>().ExportAsync(new PortableSnapshotExportRequest
        {
            ScopeMode = PortableExportScopeMode.SelectedResources,
            ResourceIds = ["manual-archived", "policy-archived", "policy-soft-deleted", "restore-target", "unmarked"],
            ResourceVersionScope = PortableResourceVersionScope.AllVersions,
        });
        var snapshot = result.Snapshot!;

        return new ExportSnapshot(
            Markers: Join(snapshot.LifecycleMarkers
                .OrderBy(static marker => marker.ResourceId, StringComparer.Ordinal)
                .Select(static marker => $"{marker.ResourceId}:{marker.State}")),
            ResourceVersions: Join(snapshot.Resources
                .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
                .ThenBy(static resource => resource.Version)
                .Select(static resource => $"{resource.ResourceId}:{resource.Version}:{resource.Id}")));
    }

    private static async Task<string> ReadAllVersionsAsync(IServiceProvider provider)
    {
        var versions = await provider.GetRequiredService<IResourceVersionReader>().ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
        });

        return Join(versions
            .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ThenBy(static resource => resource.Version)
            .Select(static resource => $"{resource.ResourceId}:{resource.Version}:{resource.Id}"));
    }

    private static async Task<string> ReadPublishedVersionsAsync(IServiceProvider provider, string resourceId)
    {
        var versions = await provider.GetRequiredService<IResourceVersionReader>().ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
            ResourceIds = [resourceId],
        });

        return Join(versions
            .OrderBy(static resource => resource.Version)
            .Select(static resource => $"{resource.ResourceId}:{resource.Version}:{resource.Id}"));
    }

    private static string Join(IEnumerable<string> values) => string.Join("|", values);

    private sealed record ProviderTransitionSnapshot(
        string ManualResult,
        string PolicyStatuses,
        string RestorePreviewStatuses,
        string RestoreStatuses,
        string ArchivedQueryIds,
        string SoftDeletedQueryIds,
        string UnmarkedQueryIds,
        string HistoryStates,
        string ExportedMarkers,
        string ExportedResourceVersions,
        bool VersionsUnchanged,
        bool ActivationUnchanged);

    private sealed record ExportSnapshot(string Markers, string ResourceVersions);
}
