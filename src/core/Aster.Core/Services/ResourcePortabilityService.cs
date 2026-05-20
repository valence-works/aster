using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;

namespace Aster.Core.Services;

/// <summary>
/// Default portability orchestration over provider-facing snapshot primitives.
/// </summary>
public sealed class ResourcePortabilityService : IResourcePortabilityService
{
    private readonly IResourcePortabilityStore portabilityStore;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePortabilityService"/>.
    /// </summary>
    public ResourcePortabilityService(IResourcePortabilityStore portabilityStore)
    {
        ArgumentNullException.ThrowIfNull(portabilityStore);
        this.portabilityStore = portabilityStore;
    }

    /// <inheritdoc />
    public async ValueTask<PortableSnapshotExportResult> ExportAsync(
        PortableSnapshotExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = ValidateExportRequest(request);
        if (diagnostics.Any(static d => d.Severity == PortableDiagnosticSeverity.Error))
            return new PortableSnapshotExportResult { Diagnostics = diagnostics };

        var storeSnapshot = await portabilityStore.ReadSnapshotAsync(
            new PortableStoreReadRequest { ExportRequest = request },
            cancellationToken);

        var skippedActivationDiagnostics = storeSnapshot.SkippedActivationEntries
            .Select(static entry => new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.SkippedActivationEntry,
                Severity = PortableDiagnosticSeverity.Warning,
                Path = $"activationStates/{entry.ResourceId}/{entry.Channel}/{entry.Version}",
                Message = $"Activation entry for resource '{entry.ResourceId}' version {entry.Version} in channel '{entry.Channel}' was skipped because that resource version is outside the export scope.",
            })
            .ToList();

        var snapshot = new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions = storeSnapshot.Definitions,
            Resources = storeSnapshot.Resources,
            ActivationStates = storeSnapshot.ActivationStates,
        };

        return new PortableSnapshotExportResult
        {
            Snapshot = snapshot,
            Diagnostics = diagnostics.Concat(skippedActivationDiagnostics).ToList(),
            SkippedActivationEntries = storeSnapshot.SkippedActivationEntries,
        };
    }

    /// <inheritdoc />
    public ValueTask<PortableSnapshotValidationResult> ValidateAsync(
        PortableSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = ValidateSnapshot(snapshot);
        return ValueTask.FromResult(new PortableSnapshotValidationResult
        {
            IsValid = diagnostics.All(static d => d.Severity != PortableDiagnosticSeverity.Error),
            Diagnostics = diagnostics,
        });
    }

    /// <inheritdoc />
    public ValueTask<PortableImportPreview> PreviewImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new PortableImportPreview
        {
            CanImport = false,
            Counts = new PortablePlannedImportCounts(),
            Diagnostics =
            [
                new PortableDiagnostic
                {
                    Code = PortableDiagnosticCodes.ImportNotImplemented,
                    Severity = PortableDiagnosticSeverity.Error,
                    Message = "Snapshot import preview is not implemented in this export-focused slice.",
                },
            ],
        });
    }

    /// <inheritdoc />
    public ValueTask<PortableImportResult> ImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new PortableImportResult
        {
            Status = PortableImportStatus.Failed,
            Counts = new PortableActualImportCounts(),
            Diagnostics =
            [
                new PortableDiagnostic
                {
                    Code = PortableDiagnosticCodes.ImportNotImplemented,
                    Severity = PortableDiagnosticSeverity.Error,
                    Message = "Snapshot import is not implemented in this export-focused slice.",
                },
            ],
        });
    }

    private static List<PortableDiagnostic> ValidateExportRequest(PortableSnapshotExportRequest request)
    {
        var diagnostics = new List<PortableDiagnostic>();

        if (!Enum.IsDefined(request.ScopeMode))
        {
            diagnostics.Add(InvalidExportScope("scopeMode", "Export scope mode must be a defined value."));
        }

        if (!Enum.IsDefined(request.ResourceVersionScope))
        {
            diagnostics.Add(InvalidExportScope("resourceVersionScope", "Resource version scope must be a defined value."));
        }

        switch (request.ScopeMode)
        {
            case PortableExportScopeMode.DefinitionsOnly:
            case PortableExportScopeMode.DefinitionWithResources:
                if (request.DefinitionIds.Count == 0)
                {
                    diagnostics.Add(InvalidExportScope(
                        "definitionIds",
                        "Definition-scoped exports require at least one definition ID."));
                }

                break;

            case PortableExportScopeMode.SelectedResources:
                if (request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions)
                {
                    if (request.SpecificResourceVersions.Count == 0)
                    {
                        diagnostics.Add(InvalidExportScope(
                            "specificResourceVersions",
                            "Specific resource version exports require at least one resource/version reference."));
                    }
                }
                else if (request.ResourceIds.Count == 0)
                {
                    diagnostics.Add(InvalidExportScope(
                        "resourceIds",
                        "Selected resource exports require at least one resource ID."));
                }

                break;
        }

        return diagnostics;
    }

    private static List<PortableDiagnostic> ValidateSnapshot(PortableSnapshot snapshot)
    {
        var diagnostics = new List<PortableDiagnostic>();

        if (snapshot.FormatVersion != PortableSnapshot.CurrentFormatVersion)
        {
            diagnostics.Add(new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.UnsupportedFormatVersion,
                Severity = PortableDiagnosticSeverity.Error,
                Path = "formatVersion",
                Message = $"Snapshot format version {snapshot.FormatVersion} is not supported.",
            });
        }

        var definitionVersions = snapshot.Definitions
            .Select(static definition => (definition.DefinitionId, definition.Version))
            .ToHashSet();

        foreach (var resource in snapshot.Resources)
        {
            if (resource.DefinitionVersion is null)
                continue;

            if (!definitionVersions.Contains((resource.DefinitionId, resource.DefinitionVersion.Value)))
            {
                diagnostics.Add(new PortableDiagnostic
                {
                    Code = PortableDiagnosticCodes.MissingDefinitionReference,
                    Severity = PortableDiagnosticSeverity.Error,
                    Path = $"resources/{resource.ResourceId}/{resource.Version}/definitionVersion",
                    Message = $"Resource '{resource.ResourceId}' version {resource.Version} references missing definition '{resource.DefinitionId}' version {resource.DefinitionVersion}.",
                });
            }
        }

        return diagnostics;
    }

    private static PortableDiagnostic InvalidExportScope(string path, string message) =>
        new()
        {
            Code = PortableDiagnosticCodes.InvalidExportScope,
            Severity = PortableDiagnosticSeverity.Error,
            Path = path,
            Message = message,
        };
}
