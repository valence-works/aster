namespace Aster.Core.Models.Portability;

/// <summary>
/// Export result containing the snapshot and any diagnostics.
/// </summary>
public sealed record PortableSnapshotExportResult
{
    /// <summary>
    /// Exported snapshot, or <see langword="null"/> when export failed validation.
    /// </summary>
    public PortableSnapshot? Snapshot { get; init; }

    /// <summary>
    /// Diagnostics emitted during export.
    /// </summary>
    public IReadOnlyList<PortableDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// Activation entries excluded because their resource versions were not exported.
    /// </summary>
    public IReadOnlyList<SkippedActivationEntry> SkippedActivationEntries { get; init; } = [];
}

/// <summary>
/// Snapshot validation result.
/// </summary>
public sealed record PortableSnapshotValidationResult
{
    /// <summary>
    /// Whether the snapshot is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Validation diagnostics.
    /// </summary>
    public IReadOnlyList<PortableDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Non-mutating import preview result.
/// </summary>
public sealed record PortableImportPreview
{
    /// <summary>
    /// Whether the snapshot can be imported.
    /// </summary>
    public required bool CanImport { get; init; }

    /// <summary>
    /// Planned import counts.
    /// </summary>
    public required PortablePlannedImportCounts Counts { get; init; }

    /// <summary>
    /// Planned identity mappings.
    /// </summary>
    public IReadOnlyList<PortableIdentityMapping> IdentityMap { get; init; } = [];

    /// <summary>
    /// Preview diagnostics.
    /// </summary>
    public IReadOnlyList<PortableDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Import result.
/// </summary>
public sealed record PortableImportResult
{
    /// <summary>
    /// Import status.
    /// </summary>
    public required PortableImportStatus Status { get; init; }

    /// <summary>
    /// Actual import counts.
    /// </summary>
    public required PortableActualImportCounts Counts { get; init; }

    /// <summary>
    /// Actual identity mappings.
    /// </summary>
    public IReadOnlyList<PortableIdentityMapping> IdentityMap { get; init; } = [];

    /// <summary>
    /// Import diagnostics.
    /// </summary>
    public IReadOnlyList<PortableDiagnostic> Diagnostics { get; init; } = [];
}
