namespace Aster.Core.Models.Instances;

/// <summary>
/// Deterministic count for one schema status.
/// </summary>
public sealed record ResourceSchemaStatusCount
{
    /// <summary>Schema status.</summary>
    public required ResourceSchemaStatus Status { get; init; }

    /// <summary>Number of inspected results with the status.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over schema status inspection results.
/// </summary>
public sealed record ResourceSchemaStatusSummary
{
    /// <summary>Total number of inspected schema status results.</summary>
    public required int TotalInspectedCount { get; init; }

    /// <summary>Number of inspected results that can be upgraded to a newer definition version.</summary>
    public required int UpgradeNeededCount { get; init; }

    /// <summary>Number of inspected results blocked by missing definition metadata.</summary>
    public required int BlockingCount { get; init; }

    /// <summary>Number of inspected results without recorded resource definition lineage.</summary>
    public required int UnknownLineageCount { get; init; }

    /// <summary>Whether no inspected results need an upgrade.</summary>
    public bool IsUpgradeFree => UpgradeNeededCount == 0;

    /// <summary>Whether any inspected results are blocked by missing definition metadata.</summary>
    public bool HasBlockingStatuses => BlockingCount > 0;

    /// <summary>Deterministic counts by schema status.</summary>
    public IReadOnlyList<ResourceSchemaStatusCount> StatusCounts { get; init; } = [];
}

/// <summary>
/// Deterministic count for one schema upgrade result status.
/// </summary>
public sealed record ResourceSchemaUpgradeStatusCount
{
    /// <summary>Schema upgrade status.</summary>
    public required ResourceSchemaUpgradeStatus Status { get; init; }

    /// <summary>Number of upgrade results with the status.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one definition version bucket.
/// </summary>
public sealed record ResourceSchemaDefinitionVersionCount
{
    /// <summary>Definition version for known buckets.</summary>
    public int? Version { get; init; }

    /// <summary>Whether this row represents unknown source definition versions.</summary>
    public required bool IsUnknown { get; init; }

    /// <summary>Number of upgrade results in the bucket.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one carried-forward aspect key.
/// </summary>
public sealed record ResourceSchemaCarriedForwardAspectKeyCount
{
    /// <summary>Nonblank carried-forward aspect key.</summary>
    public required string AspectKey { get; init; }

    /// <summary>Number of times the aspect key was carried forward.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over schema upgrade results.
/// </summary>
public sealed record ResourceSchemaUpgradeSummary
{
    /// <summary>Total number of processed schema upgrade results.</summary>
    public required int TotalProcessedCount { get; init; }

    /// <summary>Number of results that produced an upgraded resource version.</summary>
    public required int UpgradedResourceCount { get; init; }

    /// <summary>Total number of nonblank carried-forward aspect key occurrences.</summary>
    public required int CarriedForwardAspectKeyCount { get; init; }

    /// <summary>Whether every processed result was a no-op.</summary>
    public bool IsNoOpOnly =>
        TotalProcessedCount > 0
        && StatusCounts.Count == 1
        && StatusCounts[0].Status == ResourceSchemaUpgradeStatus.NoOp;

    /// <summary>Whether any processed result upgraded a resource.</summary>
    public bool HasUpgrades => StatusCounts.Any(static count => count.Status == ResourceSchemaUpgradeStatus.Upgraded && count.Count > 0);

    /// <summary>Deterministic counts by schema upgrade status.</summary>
    public IReadOnlyList<ResourceSchemaUpgradeStatusCount> StatusCounts { get; init; } = [];

    /// <summary>Deterministic counts by source definition version, including unknown source versions.</summary>
    public IReadOnlyList<ResourceSchemaDefinitionVersionCount> SourceDefinitionVersionCounts { get; init; } = [];

    /// <summary>Deterministic counts by target definition version.</summary>
    public IReadOnlyList<ResourceSchemaDefinitionVersionCount> TargetDefinitionVersionCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank carried-forward aspect key.</summary>
    public IReadOnlyList<ResourceSchemaCarriedForwardAspectKeyCount> CarriedForwardAspectKeyCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for schema status and schema upgrade result objects.
/// </summary>
public static class ResourceSchemaUpgradeSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for schema status inspection results.
    /// </summary>
    /// <param name="results">The schema status results to summarize.</param>
    /// <returns>A summary over schema status readiness and blocking counts.</returns>
    public static ResourceSchemaStatusSummary ToSummary(this IEnumerable<ResourceSchemaStatusResult>? results)
    {
        var materialized = (results ?? []).ToList();

        return new ResourceSchemaStatusSummary
        {
            TotalInspectedCount = materialized.Count,
            UpgradeNeededCount = materialized.Count(static result => result.Status == ResourceSchemaStatus.OlderThanLatest),
            BlockingCount = materialized.Count(static result =>
                result.Status is ResourceSchemaStatus.MissingDefinition or ResourceSchemaStatus.MissingDefinitionVersion),
            UnknownLineageCount = materialized.Count(static result => result.Status == ResourceSchemaStatus.UnknownResourceLineage),
            StatusCounts = materialized
                .GroupBy(static result => result.Status)
                .OrderBy(static group => group.Key)
                .Select(static group => new ResourceSchemaStatusCount
                {
                    Status = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for schema upgrade results.
    /// </summary>
    /// <param name="results">The schema upgrade results to summarize.</param>
    /// <returns>A summary over schema upgrade outcomes, versions, and carried-forward aspect keys.</returns>
    public static ResourceSchemaUpgradeSummary ToSummary(this IEnumerable<ResourceSchemaUpgradeResult>? results)
    {
        var materialized = (results ?? []).ToList();
        var carriedForwardAspectKeys = materialized
            .SelectMany(static result => result.CarriedForwardAspectKeys ?? [])
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .ToList();

        return new ResourceSchemaUpgradeSummary
        {
            TotalProcessedCount = materialized.Count,
            UpgradedResourceCount = materialized.Count(static result =>
                result.Status == ResourceSchemaUpgradeStatus.Upgraded && result.Resource is not null),
            CarriedForwardAspectKeyCount = carriedForwardAspectKeys.Count,
            StatusCounts = materialized
                .GroupBy(static result => result.Status)
                .OrderBy(static group => group.Key)
                .Select(static group => new ResourceSchemaUpgradeStatusCount
                {
                    Status = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
            SourceDefinitionVersionCounts = CountDefinitionVersions(
                materialized.Select(static result => result.SourceDefinitionVersion),
                includeUnknown: true),
            TargetDefinitionVersionCounts = CountDefinitionVersions(
                materialized.Select(static result => (int?)result.TargetDefinitionVersion),
                includeUnknown: false),
            CarriedForwardAspectKeyCounts = carriedForwardAspectKeys
                .GroupBy(static key => key, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => new ResourceSchemaCarriedForwardAspectKeyCount
                {
                    AspectKey = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
        };
    }

    private static IReadOnlyList<ResourceSchemaDefinitionVersionCount> CountDefinitionVersions(
        IEnumerable<int?> versions,
        bool includeUnknown)
    {
        var materialized = versions.ToList();
        var counts = new List<ResourceSchemaDefinitionVersionCount>();
        var unknownCount = materialized.Count(static version => !version.HasValue);
        if (includeUnknown && unknownCount > 0)
        {
            counts.Add(new ResourceSchemaDefinitionVersionCount
            {
                IsUnknown = true,
                Count = unknownCount,
            });
        }

        counts.AddRange(materialized
            .Where(static version => version.HasValue)
            .GroupBy(static version => version!.Value)
            .OrderBy(static group => group.Key)
            .Select(static group => new ResourceSchemaDefinitionVersionCount
            {
                Version = group.Key,
                IsUnknown = false,
                Count = group.Count(),
            }));

        return counts;
    }
}
