using Aster.Core.Models.Tenancy;

namespace Aster.Core.Models.Portability;

/// <summary>
/// Deterministic count for one portability diagnostic severity.
/// </summary>
public sealed record PortableDiagnosticSeverityCount
{
    /// <summary>Diagnostic severity.</summary>
    public required PortableDiagnosticSeverity Severity { get; init; }

    /// <summary>Number of diagnostics with the severity.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one portability diagnostic code.
/// </summary>
public sealed record PortableDiagnosticCodeCount
{
    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }

    /// <summary>Number of diagnostics with the code.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Deterministic count for one identity mapping reason.
/// </summary>
public sealed record PortableIdentityMappingReasonCount
{
    /// <summary>Identity mapping reason.</summary>
    public required PortableIdentityMappingReason Reason { get; init; }

    /// <summary>Number of mappings with the reason.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// Aggregate view over a portable snapshot export result.
/// </summary>
public sealed record PortableExportSummary
{
    /// <summary>Source tenant scope used by the export result.</summary>
    public TenantScope SourceTenantScope { get; init; } = TenantScope.Default;

    /// <summary>Whether the export result includes a snapshot.</summary>
    public required bool HasSnapshot { get; init; }

    /// <summary>Whether export diagnostics include at least one error.</summary>
    public bool HasErrors => DiagnosticSeverityCounts.Any(static count => count.Severity == PortableDiagnosticSeverity.Error && count.Count > 0);

    /// <summary>Number of definition snapshots exported.</summary>
    public required int DefinitionCount { get; init; }

    /// <summary>Number of resource version snapshots exported.</summary>
    public required int ResourceVersionCount { get; init; }

    /// <summary>Number of activation entries exported.</summary>
    public required int ActivationEntryCount { get; init; }

    /// <summary>Number of lifecycle markers exported.</summary>
    public required int LifecycleMarkerCount { get; init; }

    /// <summary>Number of activation entries skipped during export.</summary>
    public required int SkippedActivationEntryCount { get; init; }

    /// <summary>Deterministic diagnostic severity counts.</summary>
    public IReadOnlyList<PortableDiagnosticSeverityCount> DiagnosticSeverityCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic code counts.</summary>
    public IReadOnlyList<PortableDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Aggregate view over a portable import preview result.
/// </summary>
public sealed record PortableImportPreviewSummary
{
    /// <summary>Source tenant scope recorded in the snapshot.</summary>
    public TenantScope SourceTenantScope { get; init; } = TenantScope.Default;

    /// <summary>Target tenant scope planned for import.</summary>
    public TenantScope TargetTenantScope { get; init; } = TenantScope.Default;

    /// <summary>Whether preview allows import.</summary>
    public required bool CanImport { get; init; }

    /// <summary>Whether preview diagnostics include at least one error.</summary>
    public bool HasErrors => DiagnosticSeverityCounts.Any(static count => count.Severity == PortableDiagnosticSeverity.Error && count.Count > 0);

    /// <summary>Planned import counts.</summary>
    public PortablePlannedImportCounts Counts { get; init; } = new();

    /// <summary>Total planned content items, excluding reuse/remap accounting fields.</summary>
    public required int TotalPlannedItemCount { get; init; }

    /// <summary>Deterministic identity mapping reason counts.</summary>
    public IReadOnlyList<PortableIdentityMappingReasonCount> MappingReasonCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic severity counts.</summary>
    public IReadOnlyList<PortableDiagnosticSeverityCount> DiagnosticSeverityCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic code counts.</summary>
    public IReadOnlyList<PortableDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Aggregate view over a portable import result.
/// </summary>
public sealed record PortableImportSummary
{
    /// <summary>Source tenant scope recorded in the snapshot.</summary>
    public TenantScope SourceTenantScope { get; init; } = TenantScope.Default;

    /// <summary>Target tenant scope used for import.</summary>
    public TenantScope TargetTenantScope { get; init; } = TenantScope.Default;

    /// <summary>Import status.</summary>
    public required PortableImportStatus Status { get; init; }

    /// <summary>Whether the import wrote content.</summary>
    public bool IsImported => Status == PortableImportStatus.Imported;

    /// <summary>Whether the import was a no-op.</summary>
    public bool IsNoOp => Status == PortableImportStatus.NoOp;

    /// <summary>Whether the import failed.</summary>
    public bool IsFailed => Status == PortableImportStatus.Failed;

    /// <summary>Whether import diagnostics include at least one error.</summary>
    public bool HasErrors => DiagnosticSeverityCounts.Any(static count => count.Severity == PortableDiagnosticSeverity.Error && count.Count > 0);

    /// <summary>Actual import counts.</summary>
    public PortableActualImportCounts Counts { get; init; } = new();

    /// <summary>Total actual content items, excluding reuse/remap accounting fields.</summary>
    public required int TotalActualItemCount { get; init; }

    /// <summary>Deterministic identity mapping reason counts.</summary>
    public IReadOnlyList<PortableIdentityMappingReasonCount> MappingReasonCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic severity counts.</summary>
    public IReadOnlyList<PortableDiagnosticSeverityCount> DiagnosticSeverityCounts { get; init; } = [];

    /// <summary>Deterministic diagnostic code counts.</summary>
    public IReadOnlyList<PortableDiagnosticCodeCount> DiagnosticCodeCounts { get; init; } = [];
}

/// <summary>
/// Pure summary helpers for portability result objects.
/// </summary>
public static class PortableResultSummaryExtensions
{
    /// <summary>
    /// Creates a deterministic aggregate summary for a portable snapshot export result.
    /// </summary>
    /// <param name="result">The export result to summarize.</param>
    /// <returns>A summary over exported snapshot content and diagnostics.</returns>
    public static PortableExportSummary ToSummary(this PortableSnapshotExportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var snapshot = result.Snapshot;
        var diagnostics = result.Diagnostics ?? [];

        return new PortableExportSummary
        {
            SourceTenantScope = result.SourceTenantScope,
            HasSnapshot = snapshot is not null,
            DefinitionCount = snapshot?.Definitions?.Count ?? 0,
            ResourceVersionCount = snapshot?.Resources?.Count ?? 0,
            ActivationEntryCount = snapshot?.ActivationStates?.Count ?? 0,
            LifecycleMarkerCount = snapshot?.LifecycleMarkers?.Count ?? 0,
            SkippedActivationEntryCount = (result.SkippedActivationEntries ?? []).Count,
            DiagnosticSeverityCounts = CountSeverities(diagnostics),
            DiagnosticCodeCounts = CountCodes(diagnostics),
        };
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for a portable import preview result.
    /// </summary>
    /// <param name="preview">The import preview to summarize.</param>
    /// <returns>A summary over planned import counts, identity mappings, and diagnostics.</returns>
    public static PortableImportPreviewSummary ToSummary(this PortableImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var counts = preview.Counts ?? new PortablePlannedImportCounts();
        var diagnostics = preview.Diagnostics ?? [];

        return new PortableImportPreviewSummary
        {
            SourceTenantScope = preview.SourceTenantScope,
            TargetTenantScope = preview.TargetTenantScope,
            CanImport = preview.CanImport,
            Counts = counts,
            TotalPlannedItemCount = TotalPlannedItems(counts),
            MappingReasonCounts = CountMappingReasons(preview.IdentityMap ?? []),
            DiagnosticSeverityCounts = CountSeverities(diagnostics),
            DiagnosticCodeCounts = CountCodes(diagnostics),
        };
    }

    /// <summary>
    /// Creates a deterministic aggregate summary for a portable import result.
    /// </summary>
    /// <param name="result">The import result to summarize.</param>
    /// <returns>A summary over actual import counts, identity mappings, status, and diagnostics.</returns>
    public static PortableImportSummary ToSummary(this PortableImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var counts = result.Counts ?? new PortableActualImportCounts();
        var diagnostics = result.Diagnostics ?? [];

        return new PortableImportSummary
        {
            SourceTenantScope = result.SourceTenantScope,
            TargetTenantScope = result.TargetTenantScope,
            Status = result.Status,
            Counts = counts,
            TotalActualItemCount = TotalActualItems(counts),
            MappingReasonCounts = CountMappingReasons(result.IdentityMap ?? []),
            DiagnosticSeverityCounts = CountSeverities(diagnostics),
            DiagnosticCodeCounts = CountCodes(diagnostics),
        };
    }

    private static int TotalPlannedItems(PortablePlannedImportCounts counts) =>
        counts.Definitions
        + counts.Resources
        + counts.ResourceVersions
        + counts.ActivationEntries
        + counts.LifecycleMarkers;

    private static int TotalActualItems(PortableActualImportCounts counts) =>
        counts.Definitions
        + counts.Resources
        + counts.ResourceVersions
        + counts.ActivationEntries
        + counts.LifecycleMarkers;

    private static IReadOnlyList<PortableIdentityMappingReasonCount> CountMappingReasons(
        IEnumerable<PortableIdentityMapping> mappings) =>
        mappings
            .GroupBy(static mapping => mapping.Reason)
            .OrderBy(static group => group.Key)
            .Select(static group => new PortableIdentityMappingReasonCount
            {
                Reason = group.Key,
                Count = group.Count(),
            })
            .ToList();

    private static IReadOnlyList<PortableDiagnosticSeverityCount> CountSeverities(
        IEnumerable<PortableDiagnostic> diagnostics) =>
        diagnostics
            .GroupBy(static diagnostic => diagnostic.Severity)
            .OrderBy(static group => group.Key)
            .Select(static group => new PortableDiagnosticSeverityCount
            {
                Severity = group.Key,
                Count = group.Count(),
            })
            .ToList();

    private static IReadOnlyList<PortableDiagnosticCodeCount> CountCodes(
        IEnumerable<PortableDiagnostic> diagnostics) =>
        diagnostics
            .Select(static diagnostic => diagnostic.Code)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .GroupBy(static code => code, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => new PortableDiagnosticCodeCount
            {
                Code = group.Key,
                Count = group.Count(),
            })
            .ToList();
}
