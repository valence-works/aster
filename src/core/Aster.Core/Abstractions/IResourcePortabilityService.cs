using Aster.Core.Models.Portability;

namespace Aster.Core.Abstractions;

/// <summary>
/// Exports, validates, previews, and imports portable Aster snapshots.
/// </summary>
public interface IResourcePortabilityService
{
    /// <summary>
    /// Exports a portable snapshot for the requested explicit scope.
    /// </summary>
    ValueTask<PortableSnapshotExportResult> ExportAsync(
        PortableSnapshotExportRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates snapshot structure and relationships before import.
    /// </summary>
    ValueTask<PortableSnapshotValidationResult> ValidateAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews an import without mutating the target store.
    /// </summary>
    ValueTask<PortableImportPreview> PreviewImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a snapshot as an all-or-nothing operation.
    /// </summary>
    ValueTask<PortableImportResult> ImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default);
}
