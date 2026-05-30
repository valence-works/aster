using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;

namespace Aster.Tests.Versioning;

public sealed class ResourceVersionHistorySummaryTests
{
    [Fact]
    public void HistorySummary_MixedVersions_AggregatesCounts()
    {
        var result = new ResourceVersionHistoryResult
        {
            TenantScope = TenantScope.FromTenantId("tenant-a"),
            ResourceId = "product-1",
            Versions =
            [
                Version(1, isLatest: false, isDraft: true, activeChannels: [], ResourceLifecycleMarkerState.Archived, ResourceVersionMaintenanceDisposition.PossibleCandidate),
                Version(2, isLatest: false, isDraft: false, activeChannels: ["Published"], ResourceLifecycleMarkerState.Archived, ResourceVersionMaintenanceDisposition.Protected),
                Version(3, isLatest: true, isDraft: true, activeChannels: [], ResourceLifecycleMarkerState.None, ResourceVersionMaintenanceDisposition.Protected),
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(result.TenantScope, summary.TenantScope);
        Assert.Equal("product-1", summary.ResourceId);
        Assert.Equal(3, summary.TotalVersionCount);
        Assert.Equal(1, summary.LatestVersionCount);
        Assert.Equal(2, summary.DraftVersionCount);
        Assert.Equal(1, summary.ActiveVersionCount);
        Assert.Equal(2, summary.ProtectedVersionCount);
        Assert.Equal(1, summary.PossibleCandidateCount);
        Assert.Equal(
            [(ResourceLifecycleMarkerState.None, 1), (ResourceLifecycleMarkerState.Archived, 2)],
            summary.LifecycleStateCounts.Select(static count => (count.State, count.Count)).ToList());
    }

    [Fact]
    public void HistorySummary_EmptyHistory_PreservesIdentityWithZeroCounts()
    {
        var summary = new ResourceVersionHistoryResult
        {
            TenantScope = TenantScope.FromTenantId("tenant-a"),
            ResourceId = "missing",
        }.ToSummary();

        Assert.Equal(TenantScope.FromTenantId("tenant-a"), summary.TenantScope);
        Assert.Equal("missing", summary.ResourceId);
        Assert.Equal(0, summary.TotalVersionCount);
        Assert.Equal(0, summary.LatestVersionCount);
        Assert.Equal(0, summary.DraftVersionCount);
        Assert.Equal(0, summary.ActiveVersionCount);
        Assert.Equal(0, summary.ProtectedVersionCount);
        Assert.Equal(0, summary.PossibleCandidateCount);
        Assert.Empty(summary.LifecycleStateCounts);
    }

    [Fact]
    public void BatchSummary_MixedHistories_AggregatesResourceAndVersionCounts()
    {
        var result = new ResourceVersionHistoryBatchResult
        {
            TenantScope = TenantScope.FromTenantId("tenant-a"),
            Histories =
            [
                new ResourceVersionHistoryResult
                {
                    TenantScope = TenantScope.FromTenantId("tenant-a"),
                    ResourceId = "product-1",
                    Versions =
                    [
                        Version(1, isLatest: false, isDraft: false, activeChannels: ["Published"], ResourceLifecycleMarkerState.Archived, ResourceVersionMaintenanceDisposition.Protected),
                        Version(2, isLatest: true, isDraft: true, activeChannels: [], ResourceLifecycleMarkerState.Archived, ResourceVersionMaintenanceDisposition.Protected),
                    ],
                },
                new ResourceVersionHistoryResult
                {
                    TenantScope = TenantScope.FromTenantId("tenant-a"),
                    ResourceId = "product-2",
                    Versions =
                    [
                        Version(1, isLatest: true, isDraft: true, activeChannels: [], ResourceLifecycleMarkerState.None, ResourceVersionMaintenanceDisposition.PossibleCandidate),
                    ],
                },
                new ResourceVersionHistoryResult
                {
                    TenantScope = TenantScope.FromTenantId("tenant-a"),
                    ResourceId = "missing",
                },
            ],
        };

        var summary = result.ToSummary();

        Assert.Equal(result.TenantScope, summary.TenantScope);
        Assert.Equal(3, summary.SelectedResourceCount);
        Assert.Equal(2, summary.ResourcesWithVersionsCount);
        Assert.Equal(1, summary.MissingResourceCount);
        Assert.Equal(3, summary.TotalVersionCount);
        Assert.Equal(1, summary.ActiveVersionCount);
        Assert.Equal(2, summary.ProtectedVersionCount);
        Assert.Equal(1, summary.PossibleCandidateCount);
        Assert.Equal(
            [(ResourceLifecycleMarkerState.None, 1), (ResourceLifecycleMarkerState.Archived, 2)],
            summary.LifecycleStateCounts.Select(static count => (count.State, count.Count)).ToList());
    }

    [Fact]
    public void BatchSummary_EmptyBatch_PreservesTenantWithZeroCounts()
    {
        var summary = new ResourceVersionHistoryBatchResult
        {
            TenantScope = TenantScope.FromTenantId("tenant-a"),
        }.ToSummary();

        Assert.Equal(TenantScope.FromTenantId("tenant-a"), summary.TenantScope);
        Assert.Equal(0, summary.SelectedResourceCount);
        Assert.Equal(0, summary.ResourcesWithVersionsCount);
        Assert.Equal(0, summary.MissingResourceCount);
        Assert.Equal(0, summary.TotalVersionCount);
        Assert.Empty(summary.LifecycleStateCounts);
    }

    [Fact]
    public void Summaries_NullInputsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => ((ResourceVersionHistoryResult)null!).ToSummary());
        Assert.Throws<ArgumentNullException>(() => ((ResourceVersionHistoryBatchResult)null!).ToSummary());
    }

    [Fact]
    public void Summaries_NullCollectionsAreTreatedAsEmpty()
    {
        var historySummary = new ResourceVersionHistoryResult
        {
            ResourceId = "manual",
            Versions = null!,
        }.ToSummary();

        var batchSummary = new ResourceVersionHistoryBatchResult
        {
            Histories =
            [
                new ResourceVersionHistoryResult
                {
                    ResourceId = "manual",
                    Versions = null!,
                },
            ],
        }.ToSummary();

        Assert.Equal(0, historySummary.TotalVersionCount);
        Assert.Equal(1, batchSummary.SelectedResourceCount);
        Assert.Equal(0, batchSummary.ResourcesWithVersionsCount);
        Assert.Equal(1, batchSummary.MissingResourceCount);
    }

    [Fact]
    public void Summaries_AreGeneratedFromManuallyConstructedResultsWithoutServices()
    {
        var historySummary = new ResourceVersionHistoryResult
        {
            ResourceId = "manual",
            Versions = [Version(1, isLatest: true, isDraft: true, activeChannels: [], ResourceLifecycleMarkerState.None, ResourceVersionMaintenanceDisposition.Protected)],
        }.ToSummary();

        var batchSummary = new ResourceVersionHistoryBatchResult
        {
            Histories =
            [
                new ResourceVersionHistoryResult
                {
                    ResourceId = "manual",
                    Versions = [Version(1, isLatest: true, isDraft: true, activeChannels: [], ResourceLifecycleMarkerState.None, ResourceVersionMaintenanceDisposition.Protected)],
                },
            ],
        }.ToSummary();

        Assert.Equal(1, historySummary.TotalVersionCount);
        Assert.Equal(1, batchSummary.TotalVersionCount);
    }

    private static ResourceVersionSummary Version(
        int version,
        bool isLatest,
        bool isDraft,
        IReadOnlyList<string> activeChannels,
        ResourceLifecycleMarkerState lifecycleState,
        ResourceVersionMaintenanceDisposition disposition) =>
        new()
        {
            ResourceVersionId = $"version-{version}",
            Version = version,
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Created = new DateTime(2026, 5, 30, 12, 0, 0, DateTimeKind.Utc).AddMinutes(version),
            IsLatest = isLatest,
            IsDraft = isDraft,
            ActiveChannels = activeChannels,
            LifecycleState = lifecycleState,
            IsProtectedFromPruning = disposition == ResourceVersionMaintenanceDisposition.Protected,
            MaintenanceDisposition = disposition,
        };
}
