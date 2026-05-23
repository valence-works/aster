using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;

namespace Aster.Core.Services;

/// <summary>
/// Default portability orchestration over provider-facing snapshot primitives.
/// </summary>
public sealed class ResourcePortabilityService : IResourcePortabilityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IResourcePortabilityStore portabilityStore;
    private readonly IResourceLifecycleHookDispatcher lifecycleHooks;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePortabilityService"/>.
    /// </summary>
    public ResourcePortabilityService(
        IResourcePortabilityStore portabilityStore,
        IResourceLifecycleHookDispatcher lifecycleHooks)
    {
        ArgumentNullException.ThrowIfNull(portabilityStore);
        ArgumentNullException.ThrowIfNull(lifecycleHooks);

        this.portabilityStore = portabilityStore;
        this.lifecycleHooks = lifecycleHooks;
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

        var operationId = Guid.NewGuid();
        try
        {
            await lifecycleHooks.InvokeBeforeExportAsync(new ResourceExportLifecycleContext
            {
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.BeforeExport,
                CancellationToken = cancellationToken,
                ExportRequest = ResourceLifecycleHookContextSnapshots.Snapshot(request),
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return new PortableSnapshotExportResult { Diagnostics = [ToPortableDiagnostic(exception)] };
        }

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

        var result = new PortableSnapshotExportResult
        {
            Snapshot = snapshot,
            Diagnostics = diagnostics.Concat(skippedActivationDiagnostics).ToList(),
            SkippedActivationEntries = storeSnapshot.SkippedActivationEntries,
        };

        try
        {
            await lifecycleHooks.InvokeAfterExportAsync(new ResourceExportLifecycleContext
            {
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.AfterExport,
                CancellationToken = cancellationToken,
                ExportRequest = ResourceLifecycleHookContextSnapshots.Snapshot(request),
                Snapshot = snapshot,
                ExportResult = result,
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            result = result with { Diagnostics = [.. result.Diagnostics, ToPortableDiagnostic(exception)] };
        }

        return result;
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

        var operationId = Guid.NewGuid();
        try
        {
            await lifecycleHooks.InvokeBeforePreviewImportAsync(new ResourceImportLifecycleContext
            {
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.BeforePreviewImport,
                CancellationToken = cancellationToken,
                Snapshot = snapshot,
                ImportOptions = ResourceLifecycleHookContextSnapshots.Snapshot(options),
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return FailedPreview(ToPortableDiagnostic(exception));
        }

        var plan = await BuildImportPlanAsync(snapshot, options, cancellationToken);
        var preview = new PortableImportPreview
        {
            CanImport = plan.Diagnostics.All(static diagnostic => diagnostic.Severity != PortableDiagnosticSeverity.Error),
            Counts = plan.PlannedCounts,
            IdentityMap = plan.IdentityMap,
            Diagnostics = plan.Diagnostics,
        };

        try
        {
            await lifecycleHooks.InvokeAfterPreviewImportAsync(new ResourceImportLifecycleContext
            {
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.AfterPreviewImport,
                CancellationToken = cancellationToken,
                Snapshot = snapshot,
                ImportOptions = ResourceLifecycleHookContextSnapshots.Snapshot(options),
                Preview = preview,
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            preview = preview with
            {
                CanImport = false,
                Diagnostics = [.. preview.Diagnostics, ToPortableDiagnostic(exception)],
            };
        }

        return preview;
    }

    /// <inheritdoc />
    public async ValueTask<PortableImportResult> ImportAsync(
        PortableSnapshot snapshot,
        PortableImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new PortableImportOptions();

        var operationId = Guid.NewGuid();
        try
        {
            await lifecycleHooks.InvokeBeforeImportAsync(new ResourceImportLifecycleContext
            {
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.BeforeImport,
                CancellationToken = cancellationToken,
                Snapshot = snapshot,
                ImportOptions = ResourceLifecycleHookContextSnapshots.Snapshot(options),
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return FailedImport(ToPortableDiagnostic(exception));
        }

        var plan = await BuildImportPlanAsync(snapshot, options, cancellationToken);
        PortableImportResult result;
        if (plan.Diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
        {
            result = new PortableImportResult
            {
                Status = PortableImportStatus.Failed,
                Counts = new PortableActualImportCounts(),
                IdentityMap = plan.IdentityMap,
                Diagnostics = plan.Diagnostics,
            };
        }
        else if (plan.HasWrites)
        {
            try
            {
                await portabilityStore.ApplyImportAsync(plan.PlannedSnapshot, cancellationToken);
                result = new PortableImportResult
                {
                    Status = PortableImportStatus.Imported,
                    Counts = plan.ActualCounts,
                    IdentityMap = plan.IdentityMap,
                    Diagnostics = plan.Diagnostics,
                };
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = new PortableImportResult
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
        else
        {
            result = new PortableImportResult
            {
                Status = PortableImportStatus.NoOp,
                Counts = plan.ActualCounts,
                IdentityMap = plan.IdentityMap,
                Diagnostics = plan.Diagnostics,
            };
        }

        try
        {
            await lifecycleHooks.InvokeAfterImportAsync(new ResourceImportLifecycleContext
            {
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.AfterImport,
                CancellationToken = cancellationToken,
                Snapshot = snapshot,
                ImportOptions = ResourceLifecycleHookContextSnapshots.Snapshot(options),
                ImportResult = result,
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            result = result with { Diagnostics = [.. result.Diagnostics, ToPortableDiagnostic(exception)] };
        }

        return result;
    }

    private static PortableImportPreview FailedPreview(PortableDiagnostic diagnostic) =>
        new()
        {
            CanImport = false,
            Counts = new PortablePlannedImportCounts(),
            Diagnostics = [diagnostic],
        };

    private static PortableImportResult FailedImport(PortableDiagnostic diagnostic) =>
        new()
        {
            Status = PortableImportStatus.Failed,
            Counts = new PortableActualImportCounts(),
            Diagnostics = [diagnostic],
        };

    private static PortableDiagnostic ToPortableDiagnostic(LifecycleHookException exception) =>
        new()
        {
            Code = exception.Code switch
            {
                LifecycleHookException.RejectedCode => PortableDiagnosticCodes.LifecycleHookRejected,
                LifecycleHookException.FailedCode => PortableDiagnosticCodes.LifecycleHookFailed,
                _ => exception.Code,
            },
            Severity = PortableDiagnosticSeverity.Error,
            Path = $"lifecycle/{LifecyclePointPath(exception.LifecyclePoint)}",
            Message = exception.Message,
        };

    private static string LifecyclePointPath(LifecyclePoint lifecyclePoint)
    {
        var name = lifecyclePoint.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private async ValueTask<ImportPlan> BuildImportPlanAsync(
        PortableSnapshot snapshot,
        PortableImportOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = ValidateSnapshot(snapshot);
        diagnostics.AddRange(ValidateImportOptions(options));
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
            return ImportPlan.Failed(diagnostics);

        var targetState = await portabilityStore.ReadTargetStateAsync(snapshot, cancellationToken);
        var identityMap = new List<PortableIdentityMapping>();
        var strictFailureIdentityMap = new List<PortableIdentityMapping>();
        var existingDefinitions = targetState.Definitions.ToDictionary(static definition => (definition.DefinitionId, definition.Version));
        var existingResources = targetState.Resources.ToDictionary(static resource => (resource.ResourceId, resource.Version));
        var existingActivationStates = targetState.ActivationStates.ToDictionary(static state => (state.ResourceId, state.Channel));
        var definitionIdsToRemap = new HashSet<string>(StringComparer.Ordinal);
        var resourceIdsToRemap = new HashSet<string>(StringComparer.Ordinal);
        var remappedCollisionDiagnostics = new List<RemappedCollisionDiagnostic>();

        foreach (var definition in snapshot.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = (definition.DefinitionId, definition.Version);
            if (!existingDefinitions.TryGetValue(key, out var existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Preserved(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));

                continue;
            }

            if (ContentEquals(definition, existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Reused(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));

                continue;
            }

            if (options.CollisionMode == PortableImportCollisionMode.Strict)
            {
                strictFailureIdentityMap.Add(Collided(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
                diagnostics.Add(DivergentCollision(
                    $"definitions/{definition.DefinitionId}/{definition.Version}",
                    $"Definition '{definition.DefinitionId}' version {definition.Version} already exists with different content."));
                continue;
            }

            definitionIdsToRemap.Add(definition.DefinitionId);
            remappedCollisionDiagnostics.Add(new RemappedCollisionDiagnostic(
                PortableEntityKind.DefinitionVersion,
                definition.DefinitionId,
                definition.Version,
                null,
                $"definitions/{definition.DefinitionId}/{definition.Version}",
                $"Definition '{definition.DefinitionId}' version {definition.Version} already exists with different content."));
        }

        foreach (var resource in snapshot.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = (resource.ResourceId, resource.Version);
            if (!existingResources.TryGetValue(key, out var existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Preserved(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));

                continue;
            }

            if (ContentEquals(resource, existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Reused(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));

                if (definitionIdsToRemap.Contains(resource.DefinitionId))
                    resourceIdsToRemap.Add(resource.ResourceId);

                continue;
            }

            if (options.CollisionMode == PortableImportCollisionMode.Strict)
            {
                strictFailureIdentityMap.Add(Collided(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));
                diagnostics.Add(DivergentCollision(
                    $"resources/{resource.ResourceId}/{resource.Version}",
                    $"Resource '{resource.ResourceId}' version {resource.Version} already exists with different content."));
                continue;
            }

            resourceIdsToRemap.Add(resource.ResourceId);
            remappedCollisionDiagnostics.Add(new RemappedCollisionDiagnostic(
                PortableEntityKind.ResourceVersion,
                resource.ResourceId,
                resource.Version,
                null,
                $"resources/{resource.ResourceId}/{resource.Version}",
                $"Resource '{resource.ResourceId}' version {resource.Version} already exists with different content."));
        }

        foreach (var state in snapshot.ActivationStates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = (state.ResourceId, state.Channel);
            if (!existingActivationStates.TryGetValue(key, out var existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Preserved(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));

                continue;
            }

            if (ContentEquals(state, existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Reused(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));

                continue;
            }

            if (options.CollisionMode == PortableImportCollisionMode.Strict)
            {
                strictFailureIdentityMap.Add(Collided(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));
                diagnostics.Add(DivergentCollision(
                    $"activationStates/{state.ResourceId}/{state.Channel}",
                    $"Activation state for resource '{state.ResourceId}' channel '{state.Channel}' already exists with different content."));
                continue;
            }

            resourceIdsToRemap.Add(state.ResourceId);
            remappedCollisionDiagnostics.Add(new RemappedCollisionDiagnostic(
                PortableEntityKind.ActivationEntry,
                state.ResourceId,
                null,
                state.Channel,
                $"activationStates/{state.ResourceId}/{state.Channel}",
                $"Activation state for resource '{state.ResourceId}' channel '{state.Channel}' already exists with different content."));
        }

        if (diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
            return ImportPlan.Failed(diagnostics, strictFailureIdentityMap);

        var definitionIdMap = BuildRemappedIdMap(
            definitionIdsToRemap,
            targetState.Definitions.Select(static definition => definition.DefinitionId),
            snapshot.Definitions.Select(static definition => definition.DefinitionId));
        var resourceIdMap = BuildRemappedIdMap(
            resourceIdsToRemap,
            targetState.Resources.Select(static resource => resource.ResourceId)
                .Concat(targetState.ActivationStates.Select(static state => state.ResourceId)),
            snapshot.Resources.Select(static resource => resource.ResourceId));

        diagnostics.AddRange(remappedCollisionDiagnostics.Select(diagnostic =>
            RemappedCollision(
                diagnostic.Path,
                diagnostic.Message,
                RemappedCollisionTargetId(diagnostic, definitionIdMap, resourceIdMap))));

        var plannedDefinitions = new List<ResourceDefinition>();
        var plannedResources = new List<Resource>();
        var plannedActivationStates = new List<ActivationState>();
        var reusedIdenticalItems = 0;

        foreach (var definition in snapshot.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (definitionIdMap.TryGetValue(definition.DefinitionId, out var targetDefinitionId))
            {
                var remappedDefinition = definition with { DefinitionId = targetDefinitionId };
                plannedDefinitions.Add(remappedDefinition);
                identityMap.Add(Remapped(
                    PortableEntityKind.DefinitionVersion,
                    DefinitionVersionId(definition),
                    DefinitionVersionId(remappedDefinition)));
                continue;
            }

            var key = (definition.DefinitionId, definition.Version);
            if (!existingDefinitions.TryGetValue(key, out _))
            {
                plannedDefinitions.Add(definition);
                identityMap.Add(Preserved(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
                continue;
            }

            reusedIdenticalItems++;
            identityMap.Add(Reused(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
        }

        foreach (var resource in snapshot.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remappedResource = resource with
            {
                ResourceId = resourceIdMap.GetValueOrDefault(resource.ResourceId, resource.ResourceId),
                DefinitionId = definitionIdMap.GetValueOrDefault(resource.DefinitionId, resource.DefinitionId),
            };

            if (resourceIdMap.ContainsKey(resource.ResourceId))
            {
                plannedResources.Add(remappedResource);
                identityMap.Add(Remapped(
                    PortableEntityKind.ResourceVersion,
                    ResourceVersionId(resource),
                    ResourceVersionId(remappedResource)));
                continue;
            }

            var key = (resource.ResourceId, resource.Version);
            if (!existingResources.TryGetValue(key, out _))
            {
                plannedResources.Add(remappedResource);
                identityMap.Add(Preserved(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));
                continue;
            }

            reusedIdenticalItems++;
            identityMap.Add(Reused(PortableEntityKind.ResourceVersion, ResourceVersionId(resource)));
        }

        foreach (var state in snapshot.ActivationStates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (resourceIdMap.TryGetValue(state.ResourceId, out var targetResourceId))
            {
                var remappedState = state with { ResourceId = targetResourceId };
                plannedActivationStates.Add(remappedState);
                identityMap.Add(Remapped(
                    PortableEntityKind.ActivationEntry,
                    ActivationEntryId(state),
                    ActivationEntryId(remappedState)));
                continue;
            }

            var key = (state.ResourceId, state.Channel);
            if (!existingActivationStates.TryGetValue(key, out _))
            {
                plannedActivationStates.Add(state);
                identityMap.Add(Preserved(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));
                continue;
            }

            reusedIdenticalItems++;
            identityMap.Add(Reused(PortableEntityKind.ActivationEntry, ActivationEntryId(state)));
        }

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
            RemappedItems = identityMap.Count(static mapping => mapping.Reason == PortableIdentityMappingReason.RemappedDivergent),
        };
        var actualCounts = new PortableActualImportCounts
        {
            Definitions = plannedCounts.Definitions,
            Resources = plannedCounts.Resources,
            ResourceVersions = plannedCounts.ResourceVersions,
            ActivationEntries = plannedCounts.ActivationEntries,
            ReusedIdenticalItems = plannedCounts.ReusedIdenticalItems,
            RemappedItems = plannedCounts.RemappedItems,
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

        var definitionIdsNull = request.DefinitionIds is null;
        var resourceIdsNull = request.ResourceIds is null;
        var specificResourceVersionsNull = request.SpecificResourceVersions is null;

        if (definitionIdsNull)
        {
            diagnostics.Add(InvalidExportScope("definitionIds", "Export definition IDs collection cannot be null."));
        }

        if (resourceIdsNull)
        {
            diagnostics.Add(InvalidExportScope("resourceIds", "Export resource IDs collection cannot be null."));
        }

        if (specificResourceVersionsNull)
        {
            diagnostics.Add(InvalidExportScope("specificResourceVersions", "Specific resource versions collection cannot be null."));
        }

        if (request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions
            && request.ScopeMode != PortableExportScopeMode.DefinitionsOnly
            && !specificResourceVersionsNull
            && request.SpecificResourceVersions!.Count == 0)
        {
            diagnostics.Add(InvalidExportScope(
                "specificResourceVersions",
                "Specific resource version exports require at least one resource/version reference."));
        }

        switch (request.ScopeMode)
        {
            case PortableExportScopeMode.DefinitionsOnly:
            case PortableExportScopeMode.DefinitionWithResources:
                if (!definitionIdsNull && request.DefinitionIds!.Count == 0)
                {
                    diagnostics.Add(InvalidExportScope(
                        "definitionIds",
                        "Definition-scoped exports require at least one definition ID."));
                }

                break;

            case PortableExportScopeMode.SelectedResources:
                if (request.ResourceVersionScope != PortableResourceVersionScope.SpecificVersions
                    && !resourceIdsNull
                    && request.ResourceIds!.Count == 0)
                {
                    diagnostics.Add(InvalidExportScope(
                        "resourceIds",
                        "Selected resource exports require at least one resource ID."));
                }

                break;
        }

        return diagnostics;
    }

    private static List<PortableDiagnostic> ValidateImportOptions(PortableImportOptions options)
    {
        var diagnostics = new List<PortableDiagnostic>();

        if (!Enum.IsDefined(options.CollisionMode))
        {
            diagnostics.Add(new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.InvalidImportOptions,
                Severity = PortableDiagnosticSeverity.Error,
                Path = "collisionMode",
                Message = "Import collision mode must be a defined value.",
            });
        }

        return diagnostics;
    }

    private static List<PortableDiagnostic> ValidateSnapshot(PortableSnapshot snapshot)
    {
        var diagnostics = new List<PortableDiagnostic>();
        var validDefinitions = new List<ResourceDefinition>();
        var validResources = new List<Resource>();
        var validActivationStates = new List<ActivationState>();

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

        var definitions = snapshot.Definitions;
        var resources = snapshot.Resources;
        var activationStates = snapshot.ActivationStates;

        if (definitions is null)
        {
            diagnostics.Add(MalformedSnapshot("definitions", "Snapshot definitions collection cannot be null."));
            definitions = [];
        }

        if (resources is null)
        {
            diagnostics.Add(MalformedSnapshot("resources", "Snapshot resources collection cannot be null."));
            resources = [];
        }

        if (activationStates is null)
        {
            diagnostics.Add(MalformedSnapshot("activationStates", "Snapshot activation states collection cannot be null."));
            activationStates = [];
        }

        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (definition is null)
            {
                diagnostics.Add(MalformedSnapshot($"definitions/{i}", "Snapshot definition entry cannot be null."));
                continue;
            }

            var isMalformed = false;
            if (string.IsNullOrWhiteSpace(definition.DefinitionId))
            {
                diagnostics.Add(MalformedSnapshot($"definitions/{i}/definitionId", "Snapshot definition ID is required."));
                isMalformed = true;
            }

            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                diagnostics.Add(MalformedSnapshot($"definitions/{i}/id", "Snapshot definition version ID is required."));
                isMalformed = true;
            }

            if (definition.Version <= 0)
            {
                diagnostics.Add(MalformedSnapshot($"definitions/{i}/version", "Snapshot definition version must be greater than zero."));
                isMalformed = true;
            }

            if (definition.AspectDefinitions is null)
            {
                diagnostics.Add(MalformedSnapshot($"definitions/{i}/aspectDefinitions", "Snapshot definition aspect definitions collection cannot be null."));
                isMalformed = true;
            }

            if (!isMalformed)
                validDefinitions.Add(definition);
        }

        for (var i = 0; i < resources.Count; i++)
        {
            var resource = resources[i];
            if (resource is null)
            {
                diagnostics.Add(MalformedSnapshot($"resources/{i}", "Snapshot resource entry cannot be null."));
                continue;
            }

            var isMalformed = false;
            if (string.IsNullOrWhiteSpace(resource.ResourceId))
            {
                diagnostics.Add(MalformedSnapshot($"resources/{i}/resourceId", "Snapshot resource ID is required."));
                isMalformed = true;
            }

            if (string.IsNullOrWhiteSpace(resource.Id))
            {
                diagnostics.Add(MalformedSnapshot($"resources/{i}/id", "Snapshot resource version ID is required."));
                isMalformed = true;
            }

            if (string.IsNullOrWhiteSpace(resource.DefinitionId))
            {
                diagnostics.Add(MalformedSnapshot($"resources/{i}/definitionId", "Snapshot resource definition ID is required."));
                isMalformed = true;
            }

            if (resource.DefinitionVersion <= 0)
            {
                diagnostics.Add(MalformedSnapshot($"resources/{i}/definitionVersion", "Snapshot resource definition version must be greater than zero when present."));
                isMalformed = true;
            }

            if (resource.Version <= 0)
            {
                diagnostics.Add(MalformedSnapshot($"resources/{i}/version", "Snapshot resource version must be greater than zero."));
                isMalformed = true;
            }

            if (resource.Aspects is null)
            {
                diagnostics.Add(MalformedSnapshot($"resources/{i}/aspects", "Snapshot resource aspects collection cannot be null."));
                isMalformed = true;
            }

            if (!isMalformed)
                validResources.Add(resource);
        }

        for (var i = 0; i < activationStates.Count; i++)
        {
            var activationState = activationStates[i];
            if (activationState is null)
            {
                diagnostics.Add(MalformedSnapshot($"activationStates/{i}", "Snapshot activation state entry cannot be null."));
                continue;
            }

            var isMalformed = false;
            if (string.IsNullOrWhiteSpace(activationState.ResourceId))
            {
                diagnostics.Add(MalformedSnapshot($"activationStates/{i}/resourceId", "Snapshot activation state resource ID is required."));
                isMalformed = true;
            }

            if (string.IsNullOrWhiteSpace(activationState.Channel))
            {
                diagnostics.Add(MalformedSnapshot($"activationStates/{i}/channel", "Snapshot activation state channel is required."));
                isMalformed = true;
            }

            if (activationState.ActiveVersions is null)
            {
                diagnostics.Add(MalformedSnapshot($"activationStates/{i}/activeVersions", "Snapshot activation state active versions collection cannot be null."));
                isMalformed = true;
            }
            else if (activationState.ActiveVersions.Any(static version => version <= 0))
            {
                diagnostics.Add(MalformedSnapshot($"activationStates/{i}/activeVersions", "Snapshot activation state active versions must be greater than zero."));
                isMalformed = true;
            }

            if (!isMalformed)
                validActivationStates.Add(activationState);
        }

        diagnostics.AddRange(ValidateSnapshotIdentityUniqueness(validDefinitions, validResources, validActivationStates));

        var definitionVersions = validDefinitions
            .Select(static definition => (definition.DefinitionId, definition.Version))
            .ToHashSet();

        foreach (var resource in validResources)
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

        var resourceVersions = validResources
            .Select(static resource => (resource.ResourceId, resource.Version))
            .ToHashSet();

        foreach (var activationState in validActivationStates)
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

    private static List<PortableDiagnostic> ValidateSnapshotIdentityUniqueness(
        IReadOnlyList<ResourceDefinition> definitions,
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ActivationState> activationStates)
    {
        var diagnostics = new List<PortableDiagnostic>();

        diagnostics.AddRange(FindDuplicateKeys(
            definitions.Select(static definition => DefinitionVersionId(definition)),
            "definitions",
            "Snapshot contains duplicate definition version identities."));
        diagnostics.AddRange(FindDuplicateKeys(
            resources.Select(static resource => ResourceVersionId(resource)),
            "resources",
            "Snapshot contains duplicate resource version identities."));
        diagnostics.AddRange(FindDuplicateKeys(
            resources.Select(static resource => resource.Id),
            "resources/id",
            "Snapshot contains duplicate version-specific resource IDs."));
        diagnostics.AddRange(FindDuplicateKeys(
            activationStates.Select(static state => ActivationEntryId(state)),
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

    private static PortableDiagnostic DivergentCollision(string path, string message) =>
        new()
        {
            Code = PortableDiagnosticCodes.DivergentIdentityCollision,
            Severity = PortableDiagnosticSeverity.Error,
            Path = path,
            Message = message,
        };

    private static PortableDiagnostic RemappedCollision(string path, string message, string targetId) =>
        new()
        {
            Code = PortableDiagnosticCodes.DivergentIdentityCollision,
            Severity = PortableDiagnosticSeverity.Warning,
            Path = path,
            Message = $"{message} Incoming content will be remapped to '{targetId}'.",
        };

    private static IReadOnlyDictionary<string, string> BuildRemappedIdMap(
        IEnumerable<string> sourceIds,
        IEnumerable<string> targetReservedIds,
        IEnumerable<string> snapshotReservedIds)
    {
        var sourceSet = sourceIds.ToHashSet(StringComparer.Ordinal);
        var reservedIds = targetReservedIds
            .Concat(snapshotReservedIds.Where(id => !sourceSet.Contains(id)))
            .ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var sourceId in sourceSet.OrderBy(static id => id, StringComparer.Ordinal))
        {
            var targetId = CreateRemappedId(sourceId, reservedIds);
            reservedIds.Add(targetId);
            map[sourceId] = targetId;
        }

        return map;
    }

    private static string CreateRemappedId(string sourceId, ISet<string> reservedIds)
    {
        var baseId = $"{sourceId}__imported";
        var candidate = baseId;
        var suffix = 2;

        while (reservedIds.Contains(candidate))
        {
            candidate = $"{baseId}{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string RemappedCollisionTargetId(
        RemappedCollisionDiagnostic diagnostic,
        IReadOnlyDictionary<string, string> definitionIdMap,
        IReadOnlyDictionary<string, string> resourceIdMap) =>
        diagnostic.EntityKind switch
        {
            PortableEntityKind.DefinitionVersion => JsonSerializer.Serialize(
                new object[] { definitionIdMap[diagnostic.SourceLogicalId], diagnostic.Version!.Value },
                JsonOptions),
            PortableEntityKind.ResourceVersion => JsonSerializer.Serialize(
                new object[] { resourceIdMap[diagnostic.SourceLogicalId], diagnostic.Version!.Value },
                JsonOptions),
            PortableEntityKind.ActivationEntry => JsonSerializer.Serialize(
                new object[] { resourceIdMap[diagnostic.SourceLogicalId], diagnostic.Channel! },
                JsonOptions),
            _ => throw new InvalidOperationException($"Unsupported remapped collision entity kind '{diagnostic.EntityKind}'."),
        };

    private static PortableDiagnostic InvalidExportScope(string path, string message) =>
        new()
        {
            Code = PortableDiagnosticCodes.InvalidExportScope,
            Severity = PortableDiagnosticSeverity.Error,
            Path = path,
            Message = message,
        };

    private static PortableDiagnostic MalformedSnapshot(string path, string message) =>
        new()
        {
            Code = PortableDiagnosticCodes.MalformedSnapshot,
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
        JsonNode? leftNode;
        JsonNode? rightNode;

        try
        {
            leftNode = JsonSerializer.SerializeToNode(left, JsonOptions);
            rightNode = JsonSerializer.SerializeToNode(right, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return false;
        }

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

    private static PortableIdentityMapping Remapped(PortableEntityKind entityKind, string sourceId, string targetId) =>
        new()
        {
            EntityKind = entityKind,
            SourceId = sourceId,
            TargetId = targetId,
            Reason = PortableIdentityMappingReason.RemappedDivergent,
        };

    private static string DefinitionVersionId(ResourceDefinition definition) =>
        JsonSerializer.Serialize(new object[] { definition.DefinitionId, definition.Version }, JsonOptions);

    private static string ResourceVersionId(Resource resource) =>
        JsonSerializer.Serialize(new object[] { resource.ResourceId, resource.Version }, JsonOptions);

    private static string ActivationEntryId(ActivationState state) =>
        JsonSerializer.Serialize(new object[] { state.ResourceId, state.Channel }, JsonOptions);

    private sealed record RemappedCollisionDiagnostic(
        PortableEntityKind EntityKind,
        string SourceLogicalId,
        int? Version,
        string? Channel,
        string Path,
        string Message);

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
