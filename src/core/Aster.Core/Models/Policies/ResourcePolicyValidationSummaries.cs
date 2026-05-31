namespace Aster.Core.Models.Policies;

/// <summary>
/// Deterministic count for one policy diagnostic path.
/// </summary>
public sealed record ResourcePolicyDiagnosticPathCount
{
    /// <summary>Diagnostic path.</summary>
    public required string Path { get; init; }

    /// <summary>Number of diagnostics with the path.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one policy identifier.
/// </summary>
public sealed record ResourcePolicyDiagnosticPolicyIdCount
{
    /// <summary>Policy identifier.</summary>
    public required string PolicyId { get; init; }

    /// <summary>Number of diagnostics with the policy identifier.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one resource identifier.
/// </summary>
public sealed record ResourcePolicyDiagnosticResourceIdCount
{
    /// <summary>Resource identifier.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Number of diagnostics with the resource identifier.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one resource version.
/// </summary>
public sealed record ResourcePolicyDiagnosticResourceVersionCount
{
    /// <summary>Resource version.</summary>
    public required int ResourceVersion { get; init; }

    /// <summary>Number of diagnostics with the resource version.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over a policy validation result.
/// </summary>
public sealed record ResourcePolicyValidationSummary
{
    /// <summary>Total number of validation diagnostics.</summary>
    public required int TotalDiagnosticCount { get; init; }

    /// <summary>Whether the validation result has no diagnostics.</summary>
    public bool IsValid => TotalDiagnosticCount == 0;

    /// <summary>Whether the validation result has one or more diagnostics.</summary>
    public bool HasDiagnostics => TotalDiagnosticCount > 0;

    /// <summary>Deterministic counts by nonblank diagnostic code.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank diagnostic path.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticPathCount> DiagnosticPathCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank policy identifier.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticPolicyIdCount> PolicyIdCounts { get; init; } = [];

    /// <summary>Deterministic counts by nonblank resource identifier.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticResourceIdCount> ResourceIdCounts { get; init; } = [];

    /// <summary>Deterministic counts by resource version.</summary>
    public IReadOnlyList<ResourcePolicyDiagnosticResourceVersionCount> ResourceVersionCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for policy validation result objects.
/// </summary>
public static class ResourcePolicyValidationSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for a policy validation result.
    /// </summary>
    /// <param name="result">The validation result to summarize.</param>
    /// <returns>A summary over the result's diagnostics.</returns>
    public static ResourcePolicyValidationSummary ToSummary(this ResourcePolicyValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var diagnostics = result.Diagnostics ?? [];

        return new ResourcePolicyValidationSummary
        {
            TotalDiagnosticCount = diagnostics.Count,
            DiagnosticCodeCounts = ResourcePolicyDiagnosticCodeCounter.Count(diagnostics),
            DiagnosticPathCounts = CountBy(
                diagnostics.Select(static diagnostic => diagnostic.Path),
                static (key, count) => new ResourcePolicyDiagnosticPathCount
                {
                    Path = key,
                    Count = count,
                }),
            PolicyIdCounts = CountBy(
                diagnostics.Select(static diagnostic => diagnostic.PolicyId),
                static (key, count) => new ResourcePolicyDiagnosticPolicyIdCount
                {
                    PolicyId = key,
                    Count = count,
                }),
            ResourceIdCounts = CountBy(
                diagnostics.Select(static diagnostic => diagnostic.ResourceId),
                static (key, count) => new ResourcePolicyDiagnosticResourceIdCount
                {
                    ResourceId = key,
                    Count = count,
                }),
            ResourceVersionCounts = diagnostics
                .Where(static diagnostic => diagnostic.ResourceVersion.HasValue)
                .GroupBy(static diagnostic => diagnostic.ResourceVersion!.Value)
                .OrderBy(static group => group.Key)
                .Select(static group => new ResourcePolicyDiagnosticResourceVersionCount
                {
                    ResourceVersion = group.Key,
                    Count = group.Count(),
                })
                .ToList(),
        };
    }

    private static IReadOnlyList<TCount> CountBy<TCount>(
        IEnumerable<string?> keys,
        Func<string, int, TCount> createCount) =>
        keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .GroupBy(static key => key!, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(group => createCount(group.Key, group.Count()))
            .ToList();
}
