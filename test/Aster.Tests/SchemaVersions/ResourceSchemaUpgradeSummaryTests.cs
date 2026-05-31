using Aster.Core.Models.Instances;

namespace Aster.Tests.SchemaVersions;

public sealed class ResourceSchemaUpgradeSummaryTests
{
    [Fact]
    public void StatusSummary_MixedResults_AggregatesStatusReadinessAndBlockingCounts()
    {
        var summary = new[]
        {
            Status(ResourceSchemaStatus.Current),
            Status(ResourceSchemaStatus.OlderThanLatest),
            Status(ResourceSchemaStatus.OlderThanLatest),
            Status(ResourceSchemaStatus.MissingDefinition),
            Status(ResourceSchemaStatus.MissingDefinitionVersion),
            Status(ResourceSchemaStatus.UnknownResourceLineage),
        }.ToSummary();

        Assert.Equal(6, summary.TotalInspectedCount);
        Assert.Equal(2, summary.UpgradeNeededCount);
        Assert.Equal(2, summary.BlockingCount);
        Assert.Equal(1, summary.UnknownLineageCount);
        Assert.False(summary.IsUpgradeFree);
        Assert.True(summary.HasBlockingStatuses);
        Assert.Equal(
            [
                (ResourceSchemaStatus.Current, 1),
                (ResourceSchemaStatus.OlderThanLatest, 2),
                (ResourceSchemaStatus.MissingDefinition, 1),
                (ResourceSchemaStatus.MissingDefinitionVersion, 1),
                (ResourceSchemaStatus.UnknownResourceLineage, 1),
            ],
            summary.StatusCounts.Select(static count => (count.Status, count.Count)).ToList());
    }

    [Fact]
    public void StatusSummary_EmptyAndNullCollections_ReturnZeroCounts()
    {
        var emptySummary = Array.Empty<ResourceSchemaStatusResult>().ToSummary();
        var nullSummary = ((IEnumerable<ResourceSchemaStatusResult>?)null).ToSummary();

        Assert.Equal(0, emptySummary.TotalInspectedCount);
        Assert.Equal(0, emptySummary.UpgradeNeededCount);
        Assert.Equal(0, emptySummary.BlockingCount);
        Assert.Equal(0, emptySummary.UnknownLineageCount);
        Assert.True(emptySummary.IsUpgradeFree);
        Assert.False(emptySummary.HasBlockingStatuses);
        Assert.Empty(emptySummary.StatusCounts);

        Assert.Equal(0, nullSummary.TotalInspectedCount);
        Assert.Empty(nullSummary.StatusCounts);
    }

    [Fact]
    public void UpgradeSummary_MixedResults_AggregatesStatusResourceAspectAndVersionCounts()
    {
        var summary = new[]
        {
            Upgrade(ResourceSchemaUpgradeStatus.Upgraded, sourceVersion: 1, targetVersion: 2, resource: Resource(version: 2), carriedForward: ["LegacyAspect", "z-aspect"]),
            Upgrade(ResourceSchemaUpgradeStatus.Upgraded, sourceVersion: 1, targetVersion: 3, resource: Resource(version: 3), carriedForward: ["LegacyAspect"]),
            Upgrade(ResourceSchemaUpgradeStatus.NoOp, sourceVersion: 3, targetVersion: 3, resource: Resource(version: 3)),
        }.ToSummary();

        Assert.Equal(3, summary.TotalProcessedCount);
        Assert.Equal(2, summary.UpgradedResourceCount);
        Assert.Equal(3, summary.CarriedForwardAspectKeyCount);
        Assert.True(summary.HasUpgrades);
        Assert.False(summary.IsNoOpOnly);
        Assert.Equal(
            [(ResourceSchemaUpgradeStatus.Upgraded, 2), (ResourceSchemaUpgradeStatus.NoOp, 1)],
            summary.StatusCounts.Select(static count => (count.Status, count.Count)).ToList());
        Assert.Equal(
            [(false, 1, 2), (false, 3, 1)],
            summary.SourceDefinitionVersionCounts.Select(static count => (count.IsUnknown, count.Version, count.Count)).ToList());
        Assert.Equal(
            [(false, 2, 1), (false, 3, 2)],
            summary.TargetDefinitionVersionCounts.Select(static count => (count.IsUnknown, count.Version, count.Count)).ToList());
        Assert.Equal(
            [("LegacyAspect", 2), ("z-aspect", 1)],
            summary.CarriedForwardAspectKeyCounts.Select(static count => (count.AspectKey, count.Count)).ToList());
    }

    [Fact]
    public void UpgradeSummary_UnknownSourceAndBlankAspectKeys_AreHandledDeterministically()
    {
        var summary = new[]
        {
            Upgrade(ResourceSchemaUpgradeStatus.Upgraded, sourceVersion: null, targetVersion: 2, resource: Resource(version: 2), carriedForward: ["", " ", "LegacyAspect"]),
            Upgrade(ResourceSchemaUpgradeStatus.NoOp, sourceVersion: null, targetVersion: 2),
            Upgrade(ResourceSchemaUpgradeStatus.NoOp, sourceVersion: 1, targetVersion: 1),
        }.ToSummary();

        Assert.Equal(
            [(true, null, 2), (false, 1, 1)],
            summary.SourceDefinitionVersionCounts.Select(static count => (count.IsUnknown, count.Version, count.Count)).ToList());
        Assert.Equal(
            [("LegacyAspect", 1)],
            summary.CarriedForwardAspectKeyCounts.Select(static count => (count.AspectKey, count.Count)).ToList());
    }

    [Fact]
    public void UpgradeSummary_EmptyAndNullCollections_ReturnZeroCounts()
    {
        var emptySummary = Array.Empty<ResourceSchemaUpgradeResult>().ToSummary();
        var nullSummary = ((IEnumerable<ResourceSchemaUpgradeResult>?)null).ToSummary();

        Assert.Equal(0, emptySummary.TotalProcessedCount);
        Assert.Equal(0, emptySummary.UpgradedResourceCount);
        Assert.Equal(0, emptySummary.CarriedForwardAspectKeyCount);
        Assert.False(emptySummary.HasUpgrades);
        Assert.False(emptySummary.IsNoOpOnly);
        Assert.Empty(emptySummary.StatusCounts);
        Assert.Empty(emptySummary.SourceDefinitionVersionCounts);
        Assert.Empty(emptySummary.TargetDefinitionVersionCounts);
        Assert.Empty(emptySummary.CarriedForwardAspectKeyCounts);

        Assert.Equal(0, nullSummary.TotalProcessedCount);
        Assert.Empty(nullSummary.StatusCounts);
    }

    [Fact]
    public void UpgradeSummary_NoOpOnly_IsReportedWhenAllProcessedResultsAreNoOp()
    {
        var summary = new[]
        {
            Upgrade(ResourceSchemaUpgradeStatus.NoOp, sourceVersion: 1, targetVersion: 1),
            Upgrade(ResourceSchemaUpgradeStatus.NoOp, sourceVersion: 2, targetVersion: 2),
        }.ToSummary();

        Assert.True(summary.IsNoOpOnly);
        Assert.False(summary.HasUpgrades);
    }

    private static ResourceSchemaStatusResult Status(ResourceSchemaStatus status) =>
        new()
        {
            ResourceId = $"resource-{status}",
            ResourceVersion = 1,
            DefinitionId = "Product",
            RecordedDefinitionVersion = status == ResourceSchemaStatus.UnknownResourceLineage ? null : 1,
            LatestDefinitionVersion = status == ResourceSchemaStatus.MissingDefinition ? null : 2,
            Status = status,
            Message = status.ToString(),
        };

    private static ResourceSchemaUpgradeResult Upgrade(
        ResourceSchemaUpgradeStatus status,
        int? sourceVersion,
        int targetVersion,
        Resource? resource = null,
        IReadOnlyList<string>? carriedForward = null) =>
        new()
        {
            Status = status,
            Resource = resource,
            SourceDefinitionVersion = sourceVersion,
            TargetDefinitionVersion = targetVersion,
            CarriedForwardAspectKeys = carriedForward ?? [],
            Message = status.ToString(),
        };

    private static Resource Resource(int version) =>
        new()
        {
            ResourceId = "product-1",
            Id = $"product-1-v{version}",
            DefinitionId = "Product",
            DefinitionVersion = version,
            Version = version,
            Created = DateTime.UtcNow,
        };
}
