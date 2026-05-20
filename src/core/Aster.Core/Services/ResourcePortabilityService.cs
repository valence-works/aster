using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Portability;

namespace Aster.Core.Services;

/// <summary>
/// Default portability orchestration over provider-facing snapshot primitives.
/// </summary>
public sealed class ResourcePortabilityService : IResourcePortabilityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    public async ValueTask<PortableImportPreview> PreviewImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new PortableImportOptions();

        var plan = await BuildImportPlanAsync(snapshot, options, cancellationToken);
        return new PortableImportPreview
        {
            CanImport = plan.Diagnostics.All(static diagnostic => diagnostic.Severity != PortableDiagnosticSeverity.Error),
            Counts = plan.PlannedCounts,
            IdentityMap = plan.IdentityMap,
            Diagnostics = plan.Diagnostics,
        };
    }

    /// <inheritdoc />
    public async ValueTask<PortableImportResult> ImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new PortableImportOptions();

        var plan = await BuildImportPlanAsync(snapshot, options, cancellationToken);
        if (plan.Diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
        {
            return new PortableImportResult
            {
                Status = PortableImportStatus.Failed,
                Counts = new PortableActualImportCounts(),
                IdentityMap = plan.IdentityMap,
                Diagnostics = plan.Diagnostics,
            };
        }

        if (plan.HasWrites)
        {
            try
            {
                await portabilityStore.ApplyImportAsync(plan.PlannedSnapshot, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new PortableImportResult
                {
                    Status = PortableImportStatus.Failed,
                    Counts = new PortableActualImportCounts(),
                    IdentityMap = plan.IdentityMap,
                    Diagnostics =
                    [
                        .. plan.Diagnostics,
                        new PortableDiagnostic
                        {
                            Code = PortableDiagnosticCodes.ImportApplyFailed,
                            Severity = PortableDiagnosticSeverity.Error,
                            Message = $"Import apply failed after planning completed: {exception.Message}",
                        },
                    ],
                };
            }
        }

        return new PortableImportResult
        {
            Status = plan.HasWrites ? PortableImportStatus.Imported : PortableImportStatus.NoOp,
            Counts = plan.ActualCounts,
            IdentityMap = plan.IdentityMap,
            Diagnostics = plan.Diagnostics,
        };
    }

    private async ValueTask<ImportPlan> BuildImportPlanAsync(
        PortableSnapshot snapshot,
        PortableImportOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = ValidateSnapshot(snapshot);
        diagnostics.AddRange(ValidateSnapshotIdentityUniqueness(snapshot));
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
            return ImportPlan.Failed(diagnostics);

        var targetState = await portabilityStore.ReadTargetStateAsync(snapshot, cancellationToken);
        var plannedDefinitions = new List<ResourceDefinition>();
        var plannedResources = new List<Resource>();
        var plannedActivationStates = new List<ActivationState>();
        var identityMap = new List<PortableIdentityMapping>();
        var reusedIdenticalItems = 0;

        var existingDefinitions = targetState.Definitions.ToDictionary(static definition => (definition.DefinitionId, definition.Version));
        foreach (var definition in snapshot.Definitions)
        {
            var key = (definition.DefinitionId, definition.Version);
            if (!existingDefinitions.TryGetValue(key, out var existing))
            {
                plannedDefinitions.Add(definition);
                identityMap.Add(Preserved(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
                continue;
            }

            if (ContentEquals(definition, existing))
            {
                reusedIdenticalItems++;
                identityMap.Add(Reused(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
                continue;
            }

            identityMap.Add(Collided(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
            diagnostics.Add(DivergentCollision(
                $"definitions/{definition.DefinitionId}/{definition.Version}",
                $"Definition '{definition.DefinitionId}' version {definition.Version} already exists with different content.",
                options.CollisionMode));
        }

        var existingResources = targetState.Resources.ToDictionary(static resource => (resource.ResourceId, resource.Version));
        foreach (var resource in snapshot.Resources)
        {
            var key = (resource.ResourceId, resource.Version);
            if (!existingResources.TryGetValue(key, out var existing))
            {
                plannedResources.Add(resource);
                identityMap.Add(Preserved(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));
                continue;
            }

            if (ContentEquals(resource, existing))
            {
                reusedIdenticalItems++;
                identityMap.Add(Reused(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));
                continue;
            }

            identityMap.Add(Collided(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));
            diagnostics.Add(DivergentCollision(
                $"resources/{resource.ResourceId}/{resource.Version}",
                $"Resource '{resource.ResourceId}' version {resource.Version} already exists with different content.",
                options.CollisionMode));
        }

        var existingActivationStates = targetState.ActivationStates.ToDictionary(static state => (state.ResourceId, state.Channel));
        foreach (var state in snapshot.ActivationStates)
        {
            var key = (state.ResourceId, state.Channel);
            if (!existingActivationStates.TryGetValue(key, out var existing))
            {
                plannedActivationStates.Add(state);
                identityMap.Add(Preserved(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));
                continue;
            }

            if (ContentEquals(state, existing))
            {
                reusedIdenticalItems++;
                identityMap.Add(Reused(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));
                continue;
            }

            identityMap.Add(Collided(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));
            diagnostics.Add(DivergentCollision(
                $"activationStates/{state.ResourceId}/{state.Channel}",
                $"Activation state for resource '{state.ResourceId}' channel '{state.Channel}' already exists with different content.",
                options.CollisionMode));
        }

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
            return ImportPlan.Failed(diagnostics, identityMap);

        var plannedSnapshot = new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            Definitions = plannedDefinitions,
            Resources = plannedResources,
            ActivationStates = plannedActivationStates,
        };
        var plannedCounts = new PortablePlannedImportCounts
        {
            Definitions = plannedDefinitions.Count,
            Resources = plannedResources.Select(static resource => resource.ResourceId).Distinct(StringComparer.Ordinal).Count(),
            ResourceVersions = plannedResources.Count,
            ActivationEntries = plannedActivationStates.Count,
            ReusedIdenticalItems = reusedIdenticalItems,
        };
        var actualCounts = new PortableActualImportCounts
        {
            Definitions = plannedCounts.Definitions,
            Resources = plannedCounts.Resources,
            ResourceVersions = plannedCounts.ResourceVersions,
            ActivationEntries = plannedCounts.ActivationEntries,
            ReusedIdenticalItems = plannedCounts.ReusedIdenticalItems,
        };

        return new ImportPlan(
            plannedSnapshot,
            plannedCounts,
            actualCounts,
            identityMap,
            diagnostics);
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

        if (request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions
            && request.ScopeMode != PortableExportScopeMode.DefinitionsOnly
            && request.SpecificResourceVersions.Count == 0)
        {
            diagnostics.Add(InvalidExportScope(
                "specificResourceVersions",
                "Specific resource version exports require at least one resource/version reference."));
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
                if (request.ResourceVersionScope != PortableResourceVersionScope.SpecificVersions
                    && request.ResourceIds.Count == 0)
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

        var resourceVersions = snapshot.Resources
            .Select(static resource => (resource.ResourceId, resource.Version))
            .ToHashSet();

        foreach (var activationState in snapshot.ActivationStates)
        {
            foreach (var version in activationState.ActiveVersions)
            {
                if (resourceVersions.Contains((activationState.ResourceId, version)))
                    continue;

                diagnostics.Add(new PortableDiagnostic
                {
                    Code = PortableDiagnosticCodes.MissingResourceReference,
                    Severity = PortableDiagnosticSeverity.Error,
                    Path = $"activationStates/{activationState.ResourceId}/{activationState.Channel}/{version}",
                    Message = $"Activation entry for resource '{activationState.ResourceId}' version {version} in channel '{activationState.Channel}' references a missing resource version.",
                });
            }
        }

        return diagnostics;
    }

    private static List<PortableDiagnostic> ValidateSnapshotIdentityUniqueness(PortableSnapshot snapshot)
    {
        var diagnostics = new List<PortableDiagnostic>();

        diagnostics.AddRange(FindDuplicateKeys(
            snapshot.Definitions.Select(static definition => DefinitionVersionId(definition)),
            "definitions",
            "Snapshot contains duplicate definition version identities."));
        diagnostics.AddRange(FindDuplicateKeys(
            snapshot.Resources.Select(static resource => ResourceVersionId(resource)),
            "resources",
            "Snapshot contains duplicate resource version identities."));
        diagnostics.AddRange(FindDuplicateKeys(
            snapshot.Resources.Select(static resource => resource.Id),
            "resources/id",
            "Snapshot contains duplicate version-specific resource IDs."));
        diagnostics.AddRange(FindDuplicateKeys(
            snapshot.ActivationStates.Select(static state => ActivationEntryId(state)),
            "activationStates",
            "Snapshot contains duplicate activation state identities."));

        return diagnostics;
    }

    private static IEnumerable<PortableDiagnostic> FindDuplicateKeys(
        IEnumerable<string> keys,
        string path,
        string message) =>
        keys
            .GroupBy(static key => key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(group => new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.DuplicateSnapshotIdentity,
                Severity = PortableDiagnosticSeverity.Error,
                Path = $"{path}/{group.Key}",
                Message = $"{message} Duplicate key: '{group.Key}'.",
            });

    private static PortableDiagnostic DivergentCollision(
        string path,
        string message,
        PortableImportCollisionMode collisionMode) =>
        collisionMode == PortableImportCollisionMode.Strict
            ? new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.DivergentIdentityCollision,
                Severity = PortableDiagnosticSeverity.Error,
                Path = path,
                Message = message,
            }
            : new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.RemapDivergentNotImplemented,
                Severity = PortableDiagnosticSeverity.Error,
                Path = path,
                Message = $"{message} RemapDivergent import is planned for the next import slice.",
            };

    private static PortableDiagnostic InvalidExportScope(string path, string message) =>
        new()
        {
            Code = PortableDiagnosticCodes.InvalidExportScope,
            Severity = PortableDiagnosticSeverity.Error,
            Path = path,
            Message = message,
        };

    private static bool ContentEquals<T>(T left, T right) =>
        (left, right) switch
        {
            (ResourceDefinition leftDefinition, ResourceDefinition rightDefinition) => ContentEquals(leftDefinition, rightDefinition),
            (Resource leftResource, Resource rightResource) => ContentEquals(leftResource, rightResource),
            (ActivationState leftState, ActivationState rightState) => ContentEquals(leftState, rightState),
            _ => JsonContentEquals(left, right),
        };

    private static bool ContentEquals(ResourceDefinition left, ResourceDefinition right) =>
        string.Equals(left.DefinitionId, right.DefinitionId, StringComparison.Ordinal)
        && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && left.Version == right.Version
        && left.IsSingleton == right.IsSingleton
        && DictionaryContentEquals(left.AspectDefinitions, right.AspectDefinitions);

    private static bool ContentEquals(Resource left, Resource right) =>
        string.Equals(left.ResourceId, right.ResourceId, StringComparison.Ordinal)
        && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && string.Equals(left.DefinitionId, right.DefinitionId, StringComparison.Ordinal)
        && left.DefinitionVersion == right.DefinitionVersion
        && left.Version == right.Version
        && left.Created == right.Created
        && string.Equals(left.Owner, right.Owner, StringComparison.Ordinal)
        && string.Equals(left.Hash, right.Hash, StringComparison.Ordinal)
        && DictionaryContentEquals(left.Aspects, right.Aspects);

    private static bool ContentEquals(ActivationState left, ActivationState right) =>
        string.Equals(left.ResourceId, right.ResourceId, StringComparison.Ordinal)
        && string.Equals(left.Channel, right.Channel, StringComparison.Ordinal)
        && left.LastUpdated == right.LastUpdated
        && left.ActiveVersions.Distinct().Order().SequenceEqual(right.ActiveVersions.Distinct().Order());

    private static bool DictionaryContentEquals<TValue>(
        IReadOnlyDictionary<string, TValue> left,
        IReadOnlyDictionary<string, TValue> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (key, leftValue) in left.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(key, out var rightValue))
                return false;

            if (!JsonContentEquals(leftValue, rightValue))
                return false;
        }

        return true;
    }

    private static bool JsonContentEquals<T>(T left, T right)
    {
        var leftNode = JsonSerializer.SerializeToNode(left, JsonOptions);
        var rightNode = JsonSerializer.SerializeToNode(right, JsonOptions);

        return JsonNode.DeepEquals(leftNode, rightNode);
    }

    private static PortableIdentityMapping Preserved(PortableEntityKind entityKind, string id) =>
        new()
        {
            EntityKind = entityKind,
            SourceId = id,
            TargetId = id,
            Reason = PortableIdentityMappingReason.Preserved,
        };

    private static PortableIdentityMapping Reused(PortableEntityKind entityKind, string id) =>
        new()
        {
            EntityKind = entityKind,
            SourceId = id,
            TargetId = id,
            Reason = PortableIdentityMappingReason.ReusedIdentical,
        };

    private static PortableIdentityMapping Collided(PortableEntityKind entityKind, string id) =>
        new()
        {
            EntityKind = entityKind,
            SourceId = id,
            TargetId = id,
            Reason = PortableIdentityMappingReason.CollidedDivergent,
        };

    private static string DefinitionVersionId(ResourceDefinition definition) =>
        $"{definition.DefinitionId}:{definition.Version}";

    private static string ResourceVersionId(Resource resource) =>
        $"{resource.ResourceId}:{resource.Version}";

    private static string ActivationEntryId(ActivationState state) =>
        $"{state.ResourceId}:{state.Channel}";

    private sealed record ImportPlan(
        PortableSnapshot PlannedSnapshot,
        PortablePlannedImportCounts PlannedCounts,
        PortableActualImportCounts ActualCounts,
        IReadOnlyList<PortableIdentityMapping> IdentityMap,
        IReadOnlyList<PortableDiagnostic> Diagnostics)
    {
        public bool HasWrites =>
            PlannedCounts.Definitions > 0
            || PlannedCounts.ResourceVersions > 0
            || PlannedCounts.ActivationEntries > 0;

        public static ImportPlan Failed(
            IReadOnlyList<PortableDiagnostic> diagnostics,
            IReadOnlyList<PortableIdentityMapping>? identityMap = null) =>
            new(
                new PortableSnapshot { FormatVersion = PortableSnapshot.CurrentFormatVersion },
                new PortablePlannedImportCounts(),
                new PortableActualImportCounts(),
                identityMap ?? [],
                diagnostics);
    }
}
