using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default portability orchestration over provider-facing snapshot primitives.
/// </summary>
internal sealed class ResourcePortabilityService : IResourcePortabilityService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IResourcePortabilityStore portabilityStore;
    private readonly IResourceLifecycleHookDispatcher lifecycleHooks;
    private readonly IResourceVersionReader? versionReader;
    private readonly ReferenceValidationMode referenceValidation;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePortabilityService"/>.
    /// </summary>
    public ResourcePortabilityService(IResourcePortabilityStore portabilityStore)
        : this(portabilityStore, NoopResourceLifecycleHookDispatcher.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePortabilityService"/>.
    /// </summary>
    /// <remarks>
    /// Without an <see cref="IResourceVersionReader"/>, import cannot see the target store, so
    /// <see cref="PortableDiagnosticCodes.DanglingReference"/> is not reported.
    /// </remarks>
    public ResourcePortabilityService(
        IResourcePortabilityStore portabilityStore,
        IResourceLifecycleHookDispatcher lifecycleHooks)
    {
        ArgumentNullException.ThrowIfNull(portabilityStore);
        ArgumentNullException.ThrowIfNull(lifecycleHooks);

        this.portabilityStore = portabilityStore;
        this.lifecycleHooks = lifecycleHooks;
        referenceValidation = ReferenceValidationMode.Strict;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ResourcePortabilityService"/> that reports dangling
    /// references on import, with the severity <paramref name="options"/> sets.
    /// </summary>
    public ResourcePortabilityService(
        IResourcePortabilityStore portabilityStore,
        IResourceLifecycleHookDispatcher lifecycleHooks,
        IResourceVersionReader versionReader,
        AsterOptions options)
        : this(portabilityStore, lifecycleHooks)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(options);

        this.versionReader = versionReader;
        referenceValidation = options.ReferenceValidation;
    }

    /// <inheritdoc />
    public async ValueTask<PortableSnapshotExportResult> ExportAsync(
        PortableSnapshotExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = ValidateExportRequest(request);
        var sourceTenant = ResolveTenantForDiagnostics(request.TenantScope, "tenantScope", diagnostics);
        if (diagnostics.Any(static d => d.Severity == PortableDiagnosticSeverity.Error))
            return new PortableSnapshotExportResult
            {
                SourceTenantScope = sourceTenant,
                Diagnostics = diagnostics,
            };

        var operationId = Guid.NewGuid();
        try
        {
            await lifecycleHooks.InvokeBeforeExportAsync(new ResourceExportLifecycleContext
            {
                TenantScope = sourceTenant,
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.BeforeExport,
                CancellationToken = cancellationToken,
                ExportRequest = ResourceLifecycleHookContextSnapshots.Snapshot(request),
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return new PortableSnapshotExportResult
            {
                SourceTenantScope = sourceTenant,
                Diagnostics = [ToPortableDiagnostic(exception)],
            };
        }

        PortableSnapshot? snapshot = null;
        PortableSnapshotExportResult result;
        try
        {
            var storeSnapshot = await portabilityStore.ReadSnapshotAsync(
                new PortableStoreReadRequest { ExportRequest = request },
                cancellationToken);
            if (request.IncludeReferencedResources)
                storeSnapshot = await AddReferencedResourcesAsync(storeSnapshot, request, sourceTenant, cancellationToken);

            var skippedActivationDiagnostics = storeSnapshot.SkippedActivationEntries
                .Select(static entry => new PortableDiagnostic
                {
                    Code = PortableDiagnosticCodes.SkippedActivationEntry,
                    Severity = PortableDiagnosticSeverity.Warning,
                    Path = $"activationStates/{entry.ResourceId}/{entry.Channel}/{entry.Version}",
                    Message = $"Activation entry for resource '{entry.ResourceId}' version {entry.Version} in channel '{entry.Channel}' was skipped because that resource version is outside the export scope.",
                })
                .ToList();

            var aspectDefinitions = storeSnapshot.AspectDefinitions
                .Select(static aspect => aspect with { IsShared = false })
                .OrderBy(static aspect => aspect.AspectDefinitionId, StringComparer.Ordinal)
                .ThenBy(static aspect => aspect.Version)
                .ToList();
            var exportedAspectVersions = aspectDefinitions
                .Select(static aspect => (aspect.AspectDefinitionId, aspect.Version))
                .ToHashSet();
            var unresolvedPinDiagnostics = SharedAspectPins(storeSnapshot.Definitions)
                .Where(pin => !exportedAspectVersions.Contains((pin.Aspect.AspectDefinitionId, pin.Aspect.Version)))
                .Select(static pin => new PortableDiagnostic
                {
                    Code = PortableDiagnosticCodes.UnresolvedAspectPin,
                    Severity = PortableDiagnosticSeverity.Warning,
                    Path = PinPath(pin.Definition, pin.Key),
                    Message = $"Definition '{pin.Definition.DefinitionId}' version {pin.Definition.Version} pins aspect definition '{pin.Aspect.AspectDefinitionId}' version {pin.Aspect.Version}, which is not in the source tenant's aspect definition store; importing the snapshot requires that version in the target tenant.",
                })
                .ToList();

            snapshot = new PortableSnapshot
            {
                FormatVersion = PortableSnapshot.CurrentFormatVersion,
                SourceTenantScope = sourceTenant,
                AspectDefinitions = aspectDefinitions,
                Definitions = storeSnapshot.Definitions.Select(definition => definition with { TenantScope = sourceTenant }).ToList(),
                Resources = storeSnapshot.Resources.Select(resource => resource with { TenantScope = sourceTenant }).ToList(),
                ActivationStates = storeSnapshot.ActivationStates.Select(state => state with { TenantScope = sourceTenant }).ToList(),
                LifecycleMarkers = storeSnapshot.LifecycleMarkers.Select(marker => marker with { TenantScope = sourceTenant }).ToList(),
            };

            result = new PortableSnapshotExportResult
            {
                SourceTenantScope = sourceTenant,
                Snapshot = snapshot,
                Diagnostics = diagnostics.Concat(unresolvedPinDiagnostics).Concat(skippedActivationDiagnostics).ToList(),
                SkippedActivationEntries = storeSnapshot.SkippedActivationEntries,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new PortableSnapshotExportResult
            {
                SourceTenantScope = sourceTenant,
                Diagnostics =
                [
                    .. diagnostics,
                    new PortableDiagnostic
                    {
                        Code = PortableDiagnosticCodes.ExportReadFailed,
                        Severity = PortableDiagnosticSeverity.Error,
                        Message = $"Export read failed after validation completed: {exception.Message}",
                    },
                ],
            };
        }

        try
        {
            await lifecycleHooks.InvokeAfterExportAsync(new ResourceExportLifecycleContext
            {
                TenantScope = sourceTenant,
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
        var sourceTenant = ResolveTenantOrDefault(snapshot.SourceTenantScope);
        var targetTenant = ResolveTenantOrDefault(options.TargetTenantScope);

        var operationId = Guid.NewGuid();
        try
        {
            await lifecycleHooks.InvokeBeforePreviewImportAsync(new ResourceImportLifecycleContext
            {
                TenantScope = targetTenant,
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.BeforePreviewImport,
                CancellationToken = cancellationToken,
                Snapshot = snapshot,
                ImportOptions = ResourceLifecycleHookContextSnapshots.Snapshot(options),
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return FailedPreview(ToPortableDiagnostic(exception), sourceTenant, targetTenant);
        }

        PortableImportPreview preview;
        try
        {
            var plan = await BuildImportPlanAsync(snapshot, options, cancellationToken);
            preview = new PortableImportPreview
            {
                SourceTenantScope = sourceTenant,
                TargetTenantScope = targetTenant,
                CanImport = plan.Diagnostics.All(static diagnostic => diagnostic.Severity != PortableDiagnosticSeverity.Error),
                Counts = plan.PlannedCounts,
                IdentityMap = plan.IdentityMap,
                Diagnostics = plan.Diagnostics,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            preview = FailedPreview(ToImportPlanningFailedDiagnostic(exception), sourceTenant, targetTenant);
        }

        try
        {
            await lifecycleHooks.InvokeAfterPreviewImportAsync(new ResourceImportLifecycleContext
            {
                TenantScope = targetTenant,
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
        var sourceTenant = ResolveTenantOrDefault(snapshot.SourceTenantScope);
        var targetTenant = ResolveTenantOrDefault(options.TargetTenantScope);

        var operationId = Guid.NewGuid();
        try
        {
            await lifecycleHooks.InvokeBeforeImportAsync(new ResourceImportLifecycleContext
            {
                TenantScope = targetTenant,
                OperationId = operationId,
                LifecyclePoint = LifecyclePoint.BeforeImport,
                CancellationToken = cancellationToken,
                Snapshot = snapshot,
                ImportOptions = ResourceLifecycleHookContextSnapshots.Snapshot(options),
            }, cancellationToken);
        }
        catch (LifecycleHookException exception)
        {
            return FailedImport(ToPortableDiagnostic(exception), sourceTenant, targetTenant);
        }

        PortableImportResult result;
        ImportPlan plan;
        try
        {
            plan = await BuildImportPlanAsync(snapshot, options, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = FailedImport(ToImportPlanningFailedDiagnostic(exception), sourceTenant, targetTenant);
            return await InvokeAfterImportAsync(operationId, snapshot, options, result, cancellationToken);
        }

        if (plan.Diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
        {
            result = new PortableImportResult
            {
                SourceTenantScope = sourceTenant,
                TargetTenantScope = targetTenant,
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
                    SourceTenantScope = sourceTenant,
                    TargetTenantScope = targetTenant,
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
                    SourceTenantScope = sourceTenant,
                    TargetTenantScope = targetTenant,
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
                SourceTenantScope = sourceTenant,
                TargetTenantScope = targetTenant,
                Status = PortableImportStatus.NoOp,
                Counts = plan.ActualCounts,
                IdentityMap = plan.IdentityMap,
                Diagnostics = plan.Diagnostics,
            };
        }

        return await InvokeAfterImportAsync(operationId, snapshot, options, result, cancellationToken);
    }

    private async ValueTask<PortableImportResult> InvokeAfterImportAsync(
        Guid operationId,
        PortableSnapshot snapshot,
        PortableImportOptions options,
        PortableImportResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleHooks.InvokeAfterImportAsync(new ResourceImportLifecycleContext
            {
                TenantScope = result.TargetTenantScope,
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

    private static PortableImportPreview FailedPreview(
        PortableDiagnostic diagnostic,
        TenantScope? sourceTenant = null,
        TenantScope? targetTenant = null) =>
        new()
        {
            SourceTenantScope = sourceTenant ?? TenantScope.Default,
            TargetTenantScope = targetTenant ?? TenantScope.Default,
            CanImport = false,
            Counts = new PortablePlannedImportCounts(),
            Diagnostics = [diagnostic],
        };

    private static PortableImportResult FailedImport(
        PortableDiagnostic diagnostic,
        TenantScope? sourceTenant = null,
        TenantScope? targetTenant = null) =>
        new()
        {
            SourceTenantScope = sourceTenant ?? TenantScope.Default,
            TargetTenantScope = targetTenant ?? TenantScope.Default,
            Status = PortableImportStatus.Failed,
            Counts = new PortableActualImportCounts(),
            Diagnostics = [diagnostic],
        };

    private static PortableDiagnostic ToImportPlanningFailedDiagnostic(Exception exception) =>
        new()
        {
            Code = PortableDiagnosticCodes.ImportPlanningFailed,
            Severity = PortableDiagnosticSeverity.Error,
            Message = $"Import planning failed after validation completed: {exception.Message}",
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
        var targetTenant = ResolveTenantForDiagnostics(options.TargetTenantScope, "targetTenantScope", diagnostics);
        if (diagnostics.Any(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error))
            return ImportPlan.Failed(diagnostics);

        var targetSnapshot = ScopeSnapshot(snapshot, targetTenant);
        var targetState = await portabilityStore.ReadTargetStateAsync(targetSnapshot, cancellationToken);
        var identityMap = new List<PortableIdentityMapping>();
        var strictFailureIdentityMap = new List<PortableIdentityMapping>();
        var existingAspects = targetState.AspectDefinitions.ToDictionary(static aspect => (aspect.AspectDefinitionId, aspect.Version));
        var snapshotAspects = snapshot.AspectDefinitions.ToDictionary(static aspect => (aspect.AspectDefinitionId, aspect.Version));
        var existingDefinitions = targetState.Definitions.ToDictionary(static definition => (definition.DefinitionId, definition.Version));
        var existingResources = targetState.Resources.ToDictionary(static resource => (resource.ResourceId, resource.Version));
        var existingActivationStates = targetState.ActivationStates.ToDictionary(static state => (state.ResourceId, state.Channel));
        var existingLifecycleMarkers = targetState.LifecycleMarkers.ToDictionary(static marker => marker.ResourceId, StringComparer.Ordinal);
        var definitionIdsToRemap = new HashSet<string>(StringComparer.Ordinal);
        var resourceIdsToRemap = new HashSet<string>(StringComparer.Ordinal);
        var remappedCollisionDiagnostics = new List<RemappedCollisionDiagnostic>();

        // Shared aspect definitions are a tenant-wide vocabulary that definitions pin by (id, version), so a divergent
        // version is never remapped: renaming it would silently fork the vocabulary and orphan every pin to it. A
        // version with the same content is reused whatever IDs its registration generated (#195).
        foreach (var aspect in snapshot.AspectDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!existingAspects.TryGetValue((aspect.AspectDefinitionId, aspect.Version), out var existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Preserved(PortableEntityKind.AspectDefinitionVersion, AspectVersionId(aspect)));

                continue;
            }

            if (AspectContentEquals(aspect, existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Reused(PortableEntityKind.AspectDefinitionVersion, AspectVersionId(aspect)));

                continue;
            }

            if (options.CollisionMode == PortableImportCollisionMode.Strict)
                strictFailureIdentityMap.Add(Collided(PortableEntityKind.AspectDefinitionVersion, AspectVersionId(aspect)));

            diagnostics.Add(DivergentCollision(
                $"aspectDefinitions/{aspect.AspectDefinitionId}/{aspect.Version}",
                $"Aspect definition '{aspect.AspectDefinitionId}' version {aspect.Version} already exists with different content. Shared aspect definitions are never remapped."));
        }

        foreach (var (definition, key, pin) in SharedAspectPins(snapshot.Definitions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Pins to versions the snapshot carries were checked against them during snapshot validation.
            var pinKey = (pin.AspectDefinitionId, pin.Version);
            if (snapshotAspects.ContainsKey(pinKey))
                continue;

            if (!existingAspects.TryGetValue(pinKey, out var existing))
            {
                diagnostics.Add(new PortableDiagnostic
                {
                    Code = PortableDiagnosticCodes.UnresolvedAspectPin,
                    Severity = PortableDiagnosticSeverity.Error,
                    Path = PinPath(definition, key),
                    Message = $"Definition '{definition.DefinitionId}' version {definition.Version} pins aspect definition '{pin.AspectDefinitionId}' version {pin.Version}, which is in neither the snapshot nor the target tenant.",
                });
                continue;
            }

            if (!AspectContentEquals(pin, existing))
                diagnostics.Add(AspectPinMismatch(definition, key, pin));
        }

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

            // Compared as it would be stored: an earlier import of it embeds the target's copies of its pins.
            if (ContentEquals(PinToTargetAspects(definition, existingAspects), existing))
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

        foreach (var marker in snapshot.LifecycleMarkers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!existingLifecycleMarkers.TryGetValue(marker.ResourceId, out var existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Preserved(PortableEntityKind.LifecycleMarker, LifecycleMarkerId(marker)));

                continue;
            }

            if (ContentEquals(marker, existing))
            {
                if (options.CollisionMode == PortableImportCollisionMode.Strict)
                    strictFailureIdentityMap.Add(Reused(PortableEntityKind.LifecycleMarker, LifecycleMarkerId(marker)));

                continue;
            }

            if (options.CollisionMode == PortableImportCollisionMode.Strict)
            {
                strictFailureIdentityMap.Add(Collided(PortableEntityKind.LifecycleMarker, LifecycleMarkerId(marker)));
                diagnostics.Add(DivergentCollision(
                    $"lifecycleMarkers/{marker.ResourceId}",
                    $"Lifecycle marker for resource '{marker.ResourceId}' already exists with different content."));
                continue;
            }

            resourceIdsToRemap.Add(marker.ResourceId);
            remappedCollisionDiagnostics.Add(new RemappedCollisionDiagnostic(
                PortableEntityKind.LifecycleMarker,
                marker.ResourceId,
                null,
                null,
                $"lifecycleMarkers/{marker.ResourceId}",
                $"Lifecycle marker for resource '{marker.ResourceId}' already exists with different content."));
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
                .Concat(targetState.ActivationStates.Select(static state => state.ResourceId))
                .Concat(targetState.LifecycleMarkers.Select(static marker => marker.ResourceId)),
            snapshot.Resources.Select(static resource => resource.ResourceId)
                .Concat(snapshot.LifecycleMarkers.Select(static marker => marker.ResourceId)));

        diagnostics.AddRange(remappedCollisionDiagnostics.Select(diagnostic =>
            RemappedCollision(
                diagnostic.Path,
                diagnostic.Message,
                RemappedCollisionTargetId(diagnostic, definitionIdMap, resourceIdMap))));

        var plannedAspects = new List<AspectDefinition>();
        var plannedDefinitions = new List<ResourceDefinition>();
        var plannedResources = new List<Resource>();
        var plannedActivationStates = new List<ActivationState>();
        var plannedLifecycleMarkers = new List<ResourceLifecycleMarker>();
        var reusedIdenticalItems = 0;

        foreach (var aspect in snapshot.AspectDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!existingAspects.ContainsKey((aspect.AspectDefinitionId, aspect.Version)))
            {
                plannedAspects.Add(aspect with { IsShared = false });
                identityMap.Add(Preserved(PortableEntityKind.AspectDefinitionVersion, AspectVersionId(aspect)));
                continue;
            }

            reusedIdenticalItems++;
            identityMap.Add(Reused(PortableEntityKind.AspectDefinitionVersion, AspectVersionId(aspect)));
        }

        foreach (var definition in snapshot.Definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (definitionIdMap.TryGetValue(definition.DefinitionId, out var targetDefinitionId))
            {
                var remappedDefinition = PinToTargetAspects(definition, existingAspects) with { DefinitionId = targetDefinitionId };
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
                plannedDefinitions.Add(PinToTargetAspects(definition, existingAspects));
                identityMap.Add(Preserved(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
                continue;
            }

            reusedIdenticalItems++;
            identityMap.Add(Reused(PortableEntityKind.DefinitionVersion, DefinitionVersionId(definition)));
        }

        var referenceFacets = new SnapshotReferenceFacets(snapshot.Definitions);
        var referenceMappings = new List<PortableIdentityMapping>();
        var plannedReferences = new List<(Resource Source, ReferenceFacetValues.ReferenceValue Value)>();

        foreach (var resource in snapshot.Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remappedResource = resource with
            {
                ResourceId = resourceIdMap.GetValueOrDefault(resource.ResourceId, resource.ResourceId),
                DefinitionId = definitionIdMap.GetValueOrDefault(resource.DefinitionId, resource.DefinitionId),
            };

            // References follow their remapped targets (#89); reused versions are not rewritten.
            var facets = referenceFacets.For(resource);
            var references = ReferenceFacetValues.Read(facets, resource.Aspects).ToList();
            if (resourceIdMap.ContainsKey(resource.ResourceId)
                || !existingResources.ContainsKey((resource.ResourceId, resource.Version)))
            {
                plannedReferences.AddRange(references.Select(value => (resource, value)));
                if (references.Any(value => resourceIdMap.ContainsKey(value.TargetResourceId)))
                {
                    remappedResource = remappedResource with
                    {
                        Aspects = new Dictionary<string, object>(
                            ReferenceFacetValues.Rewrite(facets, resource.Aspects, resourceIdMap),
                            StringComparer.Ordinal),
                    };
                    referenceMappings.AddRange(references
                        .Where(value => resourceIdMap.ContainsKey(value.TargetResourceId))
                        .Select(value => new PortableIdentityMapping
                        {
                            EntityKind = PortableEntityKind.ReferenceValue,
                            SourceId = value.TargetResourceId,
                            TargetId = resourceIdMap[value.TargetResourceId],
                            Reason = PortableIdentityMappingReason.RemappedDivergent,
                            Location = ReferenceLocation(resource, value),
                        }));
                }
            }

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

        foreach (var marker in snapshot.LifecycleMarkers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (resourceIdMap.TryGetValue(marker.ResourceId, out var targetResourceId))
            {
                var remappedMarker = marker with { ResourceId = targetResourceId };
                plannedLifecycleMarkers.Add(remappedMarker);
                identityMap.Add(Remapped(
                    PortableEntityKind.LifecycleMarker,
                    LifecycleMarkerId(marker),
                    LifecycleMarkerId(remappedMarker)));
                continue;
            }

            if (!existingLifecycleMarkers.TryGetValue(marker.ResourceId, out _))
            {
                plannedLifecycleMarkers.Add(marker);
                identityMap.Add(Preserved(PortableEntityKind.LifecycleMarker, LifecycleMarkerId(marker)));
                continue;
            }

            reusedIdenticalItems++;
            identityMap.Add(Reused(PortableEntityKind.LifecycleMarker, LifecycleMarkerId(marker)));
        }

        identityMap.AddRange(referenceMappings);
        diagnostics.AddRange(await FindDanglingReferencesAsync(snapshot, plannedReferences, targetTenant, cancellationToken));

        var plannedSnapshot = new PortableSnapshot
        {
            FormatVersion = PortableSnapshot.CurrentFormatVersion,
            SourceTenantScope = targetTenant,
            AspectDefinitions = plannedAspects,
            Definitions = plannedDefinitions.Select(definition => definition with { TenantScope = targetTenant }).ToList(),
            Resources = plannedResources.Select(resource => resource with { TenantScope = targetTenant }).ToList(),
            ActivationStates = plannedActivationStates.Select(state => state with { TenantScope = targetTenant }).ToList(),
            LifecycleMarkers = plannedLifecycleMarkers.Select(marker => marker with { TenantScope = targetTenant }).ToList(),
        };
        var plannedCounts = new PortablePlannedImportCounts
        {
            AspectDefinitions = plannedAspects.Count,
            Definitions = plannedDefinitions.Count,
            Resources = plannedResources.Select(static resource => resource.ResourceId).Distinct(StringComparer.Ordinal).Count(),
            ResourceVersions = plannedResources.Count,
            ActivationEntries = plannedActivationStates.Count,
            LifecycleMarkers = plannedLifecycleMarkers.Count,
            ReusedIdenticalItems = reusedIdenticalItems,
            RemappedItems = identityMap.Count(static mapping =>
                mapping.Reason == PortableIdentityMappingReason.RemappedDivergent
                && mapping.EntityKind != PortableEntityKind.ReferenceValue),
        };
        var actualCounts = new PortableActualImportCounts
        {
            AspectDefinitions = plannedCounts.AspectDefinitions,
            Definitions = plannedCounts.Definitions,
            Resources = plannedCounts.Resources,
            ResourceVersions = plannedCounts.ResourceVersions,
            ActivationEntries = plannedCounts.ActivationEntries,
            LifecycleMarkers = plannedCounts.LifecycleMarkers,
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

    /// <summary>
    /// Reports planned reference values whose target is neither in the snapshot nor in the target tenant,
    /// in snapshot order. Needs the version reader; without it nothing is reported.
    /// </summary>
    private async ValueTask<IReadOnlyList<PortableDiagnostic>> FindDanglingReferencesAsync(
        PortableSnapshot snapshot,
        IReadOnlyList<(Resource Source, ReferenceFacetValues.ReferenceValue Value)> plannedReferences,
        TenantScope targetTenant,
        CancellationToken cancellationToken)
    {
        if (versionReader is null || plannedReferences.Count == 0)
            return [];

        var snapshotResourceIds = snapshot.Resources.Select(static resource => resource.ResourceId).ToHashSet(StringComparer.Ordinal);
        var outside = plannedReferences
            .Select(static reference => reference.Value.TargetResourceId)
            .Where(id => !snapshotResourceIds.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        if (outside.Count == 0)
            return [];

        var existing = (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = targetTenant,
            Scope = ResourceVersionScope.Latest,
            ResourceIds = outside,
        }, cancellationToken)).Select(static resource => resource.ResourceId).ToHashSet(StringComparer.Ordinal);

        var severity = referenceValidation == ReferenceValidationMode.Strict
            ? PortableDiagnosticSeverity.Error
            : PortableDiagnosticSeverity.Warning;

        return plannedReferences
            .Where(reference => outside.Contains(reference.Value.TargetResourceId) && !existing.Contains(reference.Value.TargetResourceId))
            .Select(reference => new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.DanglingReference,
                Severity = severity,
                Path = ReferenceLocation(reference.Source, reference.Value),
                Message = $"Resource '{reference.Source.ResourceId}' version {reference.Source.Version} references '{reference.Value.TargetResourceId}', which is neither in the snapshot nor in the target tenant.",
            })
            .ToList();
    }

    /// <summary>
    /// Adds the resources that exported resources reference, transitively, reading each round of newly
    /// referenced IDs through the store's selected-resource export. IDs that do not exist are tried once.
    /// </summary>
    private async ValueTask<PortableStoreSnapshot> AddReferencedResourcesAsync(
        PortableStoreSnapshot storeSnapshot,
        PortableSnapshotExportRequest request,
        TenantScope sourceTenant,
        CancellationToken cancellationToken)
    {
        var attempted = storeSnapshot.Resources.Select(static resource => resource.ResourceId).ToHashSet(StringComparer.Ordinal);
        var scanned = 0;

        while (true)
        {
            var referenceFacets = new SnapshotReferenceFacets(storeSnapshot.Definitions);
            var missing = storeSnapshot.Resources
                .Skip(scanned)
                .SelectMany(resource => ReferenceFacetValues.Read(referenceFacets.For(resource), resource.Aspects))
                .Select(static value => value.TargetResourceId)
                .Where(attempted.Add)
                .ToHashSet(StringComparer.Ordinal);
            scanned = storeSnapshot.Resources.Count;
            if (missing.Count == 0)
                return storeSnapshot;

            var added = await portabilityStore.ReadSnapshotAsync(new PortableStoreReadRequest
            {
                ExportRequest = new PortableSnapshotExportRequest
                {
                    TenantScope = sourceTenant,
                    ScopeMode = PortableExportScopeMode.SelectedResources,
                    ResourceIds = missing,
                    ResourceVersionScope = request.ResourceVersionScope == PortableResourceVersionScope.SpecificVersions
                        ? PortableResourceVersionScope.LatestOnly
                        : request.ResourceVersionScope,
                },
            }, cancellationToken);

            // Merge every collection, shared aspect versions included: the pins of added definitions must travel too.
            storeSnapshot = storeSnapshot with
            {
                AspectDefinitions = storeSnapshot.AspectDefinitions
                    .Concat(added.AspectDefinitions)
                    .DistinctBy(static aspect => (aspect.AspectDefinitionId, aspect.Version))
                    .ToList(),
                Definitions = storeSnapshot.Definitions
                    .Concat(added.Definitions)
                    .DistinctBy(static definition => (definition.DefinitionId, definition.Version))
                    .ToList(),
                Resources = storeSnapshot.Resources
                    .Concat(added.Resources)
                    .DistinctBy(static resource => (resource.ResourceId, resource.Version))
                    .ToList(),
                ActivationStates = storeSnapshot.ActivationStates
                    .Concat(added.ActivationStates)
                    .DistinctBy(static state => (state.ResourceId, state.Channel))
                    .ToList(),
                LifecycleMarkers = storeSnapshot.LifecycleMarkers
                    .Concat(added.LifecycleMarkers)
                    .DistinctBy(static marker => marker.ResourceId, StringComparer.Ordinal)
                    .ToList(),
                SkippedActivationEntries = storeSnapshot.SkippedActivationEntries
                    .Concat(added.SkippedActivationEntries)
                    .ToList(),
            };
        }
    }

    private static string ReferenceLocation(Resource source, ReferenceFacetValues.ReferenceValue value) =>
        $"resources/{source.ResourceId}/{source.Version}/{value.Facet.AspectKey}/{value.Facet.FacetDefinitionId}"
        + (value.ElementIndex is { } index ? $"/{index}" : string.Empty);

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
        var sourceTenant = ResolveTenantForDiagnostics(snapshot.SourceTenantScope, "sourceTenantScope", diagnostics);
        var validAspects = new List<AspectDefinition>();
        var validDefinitions = new List<ResourceDefinition>();
        var validResources = new List<Resource>();
        var validActivationStates = new List<ActivationState>();
        var validLifecycleMarkers = new List<ResourceLifecycleMarker>();

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

        var aspectDefinitions = snapshot.AspectDefinitions;
        var definitions = snapshot.Definitions;
        var resources = snapshot.Resources;
        var activationStates = snapshot.ActivationStates;
        var lifecycleMarkers = snapshot.LifecycleMarkers;

        if (aspectDefinitions is null)
        {
            diagnostics.Add(MalformedSnapshot("aspectDefinitions", "Snapshot aspect definitions collection cannot be null."));
            aspectDefinitions = [];
        }

        if (definitions is null)
        {
            diagnostics.Add(MalformedSnapshot("definitions", "Snapshot definitions collection cannot be null."));
            definitions = [];
        }

        for (var i = 0; i < aspectDefinitions.Count; i++)
        {
            var aspect = aspectDefinitions[i];
            if (aspect is null)
            {
                diagnostics.Add(MalformedSnapshot($"aspectDefinitions/{i}", "Snapshot aspect definition entry cannot be null."));
                continue;
            }

            var isMalformed = false;
            if (string.IsNullOrWhiteSpace(aspect.AspectDefinitionId))
            {
                diagnostics.Add(MalformedSnapshot($"aspectDefinitions/{i}/aspectDefinitionId", "Snapshot aspect definition ID is required."));
                isMalformed = true;
            }

            if (string.IsNullOrWhiteSpace(aspect.Id))
            {
                diagnostics.Add(MalformedSnapshot($"aspectDefinitions/{i}/id", "Snapshot aspect definition version ID is required."));
                isMalformed = true;
            }

            if (aspect.Version <= 0)
            {
                diagnostics.Add(MalformedSnapshot($"aspectDefinitions/{i}/version", "Snapshot aspect definition version must be greater than zero."));
                isMalformed = true;
            }

            if (aspect.FacetDefinitions is null)
            {
                diagnostics.Add(MalformedSnapshot($"aspectDefinitions/{i}/facetDefinitions", "Snapshot aspect definition facet definitions collection cannot be null."));
                isMalformed = true;
            }
            else if (aspect.FacetDefinitions.Any(static facet => facet is null))
            {
                diagnostics.Add(MalformedSnapshot($"aspectDefinitions/{i}/facetDefinitions", "Snapshot aspect definition facet definition entries cannot be null."));
                isMalformed = true;
            }

            if (!isMalformed)
                validAspects.Add(aspect);
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

        if (lifecycleMarkers is null)
        {
            diagnostics.Add(MalformedSnapshot("lifecycleMarkers", "Snapshot lifecycle markers collection cannot be null."));
            lifecycleMarkers = [];
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
            {
                validDefinitions.Add(definition);
                ValidateEntityTenant(definition.TenantScope, sourceTenant, $"definitions/{i}/tenantScope", diagnostics);
            }
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
            {
                validResources.Add(resource);
                ValidateEntityTenant(resource.TenantScope, sourceTenant, $"resources/{i}/tenantScope", diagnostics);
            }
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
            {
                validActivationStates.Add(activationState);
                ValidateEntityTenant(activationState.TenantScope, sourceTenant, $"activationStates/{i}/tenantScope", diagnostics);
            }
        }

        for (var i = 0; i < lifecycleMarkers.Count; i++)
        {
            var marker = lifecycleMarkers[i];
            if (marker is null)
            {
                diagnostics.Add(MalformedSnapshot($"lifecycleMarkers/{i}", "Snapshot lifecycle marker entry cannot be null."));
                continue;
            }

            var isMalformed = false;
            if (string.IsNullOrWhiteSpace(marker.ResourceId))
            {
                diagnostics.Add(MalformedSnapshot($"lifecycleMarkers/{i}/resourceId", "Snapshot lifecycle marker resource ID is required."));
                isMalformed = true;
            }

            if (marker.State is ResourceLifecycleMarkerState.None || !Enum.IsDefined(marker.State))
            {
                diagnostics.Add(MalformedSnapshot($"lifecycleMarkers/{i}/state", "Snapshot lifecycle marker state must be archived or soft-deleted."));
                isMalformed = true;
            }

            if (!isMalformed)
            {
                validLifecycleMarkers.Add(marker);
                ValidateEntityTenant(marker.TenantScope, sourceTenant, $"lifecycleMarkers/{i}/tenantScope", diagnostics);
            }
        }

        diagnostics.AddRange(ValidateSnapshotIdentityUniqueness(validDefinitions, validResources, validActivationStates, validLifecycleMarkers));
        diagnostics.AddRange(FindDuplicateKeys(
            validAspects.Select(static aspect => AspectVersionId(aspect)),
            "aspectDefinitions",
            "Snapshot contains duplicate aspect definition version identities."));

        var aspectVersions = validAspects
            .GroupBy(static aspect => (aspect.AspectDefinitionId, aspect.Version))
            .ToDictionary(static group => group.Key, static group => group.First());

        foreach (var (definition, key, pin) in SharedAspectPins(validDefinitions))
        {
            if (string.IsNullOrWhiteSpace(pin.AspectDefinitionId) || pin.Version <= 0)
            {
                diagnostics.Add(MalformedSnapshot(PinPath(definition, key), "A pinned shared aspect requires an aspect definition ID and a version greater than zero."));
                continue;
            }

            if (pin.FacetDefinitions is null || pin.FacetDefinitions.Any(static facet => facet is null))
            {
                diagnostics.Add(MalformedSnapshot($"{PinPath(definition, key)}/facetDefinitions", "A pinned shared aspect's facet definitions collection and its entries cannot be null."));
                continue;
            }

            if (aspectVersions.TryGetValue((pin.AspectDefinitionId, pin.Version), out var aspect) && !AspectContentEquals(pin, aspect))
                diagnostics.Add(AspectPinMismatch(definition, key, pin));
        }

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

        var resourceIds = validResources
            .Select(static resource => resource.ResourceId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var marker in validLifecycleMarkers)
        {
            if (resourceIds.Contains(marker.ResourceId))
                continue;

            diagnostics.Add(new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.MissingResourceReference,
                Severity = PortableDiagnosticSeverity.Error,
                Path = $"lifecycleMarkers/{marker.ResourceId}",
                Message = $"Lifecycle marker for resource '{marker.ResourceId}' references a missing resource.",
            });
        }

        return diagnostics;
    }

    private static List<PortableDiagnostic> ValidateSnapshotIdentityUniqueness(
        IReadOnlyList<ResourceDefinition> definitions,
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ActivationState> activationStates,
        IReadOnlyList<ResourceLifecycleMarker> lifecycleMarkers)
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
        diagnostics.AddRange(FindDuplicateKeys(
            lifecycleMarkers.Select(static marker => LifecycleMarkerId(marker)),
            "lifecycleMarkers",
            "Snapshot contains duplicate lifecycle marker identities."));

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
            PortableEntityKind.LifecycleMarker => JsonSerializer.Serialize(
                new object[] { resourceIdMap[diagnostic.SourceLogicalId] },
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

    private static TenantScope ResolveTenantOrDefault(TenantScope? tenantScope)
    {
        try
        {
            return TenantScopeResolver.Resolve(tenantScope);
        }
        catch (TenantScopeException)
        {
            return TenantScope.Default;
        }
    }

    private static TenantScope ResolveTenantForDiagnostics(
        TenantScope? tenantScope,
        string path,
        ICollection<PortableDiagnostic> diagnostics)
    {
        try
        {
            return TenantScopeResolver.Resolve(tenantScope);
        }
        catch (TenantScopeException exception)
        {
            diagnostics.Add(new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.InvalidTenantScope,
                Severity = PortableDiagnosticSeverity.Error,
                Path = path,
                Message = exception.Message,
            });

            return TenantScope.Default;
        }
    }

    private static void ValidateEntityTenant(
        TenantScope? entityTenantScope,
        TenantScope sourceTenant,
        string path,
        ICollection<PortableDiagnostic> diagnostics)
    {
        var entityTenant = ResolveTenantForDiagnostics(entityTenantScope, path, diagnostics);
        if (diagnostics.Any(d =>
            string.Equals(d.Path, path, StringComparison.Ordinal)
            && d.Code == PortableDiagnosticCodes.InvalidTenantScope))
            return;

        if (!string.Equals(entityTenant.TenantId, sourceTenant.TenantId, StringComparison.Ordinal))
        {
            diagnostics.Add(new PortableDiagnostic
            {
                Code = PortableDiagnosticCodes.SourceTenantScopeMismatch,
                Severity = PortableDiagnosticSeverity.Error,
                Path = path,
                Message = $"Snapshot entry tenant '{entityTenant.TenantId}' does not match source tenant '{sourceTenant.TenantId}'.",
            });
        }
    }

    private static PortableSnapshot ScopeSnapshot(PortableSnapshot snapshot, TenantScope tenantScope) =>
        snapshot with
        {
            SourceTenantScope = tenantScope,
            Definitions = snapshot.Definitions.Select(definition => definition with { TenantScope = tenantScope }).ToList(),
            Resources = snapshot.Resources.Select(resource => resource with { TenantScope = tenantScope }).ToList(),
            ActivationStates = snapshot.ActivationStates.Select(state => state with { TenantScope = tenantScope }).ToList(),
            LifecycleMarkers = snapshot.LifecycleMarkers.Select(marker => marker with { TenantScope = tenantScope }).ToList(),
        };

    private static bool ContentEquals<T>(T left, T right) =>
        (left, right) switch
        {
            (ResourceDefinition leftDefinition, ResourceDefinition rightDefinition) => ContentEquals(leftDefinition, rightDefinition),
            (Resource leftResource, Resource rightResource) => ContentEquals(leftResource, rightResource),
            (ActivationState leftState, ActivationState rightState) => ContentEquals(leftState, rightState),
            (ResourceLifecycleMarker leftMarker, ResourceLifecycleMarker rightMarker) => ContentEquals(leftMarker, rightMarker),
            _ => JsonContentEquals(left, right),
        };

    private static bool ContentEquals(ResourceDefinition left, ResourceDefinition right) =>
        string.Equals(left.DefinitionId, right.DefinitionId, StringComparison.Ordinal)
        && string.Equals(left.Id, right.Id, StringComparison.Ordinal)
        && left.Version == right.Version
        && left.IsSingleton == right.IsSingleton
        && DictionaryContentEquals(left.AspectDefinitions, right.AspectDefinitions)
        && left.PolicyDeclarations.Count == right.PolicyDeclarations.Count
        && left.PolicyDeclarations
            .OrderBy(static policy => policy.PolicyId, StringComparer.Ordinal)
            .Zip(right.PolicyDeclarations.OrderBy(static policy => policy.PolicyId, StringComparer.Ordinal))
            .All(pair => JsonContentEquals(pair.First, pair.Second));

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

    private static bool ContentEquals(ResourceLifecycleMarker left, ResourceLifecycleMarker right) =>
        string.Equals(left.ResourceId, right.ResourceId, StringComparison.Ordinal)
        && left.State == right.State
        && left.MarkedAt == right.MarkedAt
        && string.Equals(left.Reason, right.Reason, StringComparison.Ordinal);

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

    private static IEnumerable<(ResourceDefinition Definition, string Key, AspectDefinition Aspect)> SharedAspectPins(
        IEnumerable<ResourceDefinition> definitions) =>
        definitions.SelectMany(static definition => definition.AspectDefinitions
            .Where(static entry => entry.Value is { IsShared: true })
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => (definition, entry.Key, entry.Value)));

    private static string PinPath(ResourceDefinition definition, string key) =>
        $"definitions/{definition.DefinitionId}/{definition.Version}/aspectDefinitions/{key}";

    private static PortableDiagnostic AspectPinMismatch(ResourceDefinition definition, string key, AspectDefinition pin) =>
        new()
        {
            Code = PortableDiagnosticCodes.AspectPinMismatch,
            Severity = PortableDiagnosticSeverity.Error,
            Path = PinPath(definition, key),
            Message = $"Definition '{definition.DefinitionId}' version {definition.Version} pins aspect definition '{pin.AspectDefinitionId}' version {pin.Version} with content that differs from that version.",
        };

    // A pin is the registered version embedded with IsShared set, so the flag is not content.
    // Shared aspects are a tenant's vocabulary, pinned by (id, version) and compared by content: the version and facet
    // IDs are generated per registration, so two tenants that register the same aspect independently still match.
    private static bool AspectContentEquals(AspectDefinition left, AspectDefinition right) =>
        JsonContentEquals(AspectContent(left), AspectContent(right));

    private static AspectDefinition AspectContent(AspectDefinition aspect) => aspect with
    {
        Id = string.Empty,
        IsShared = false,
        FacetDefinitions = aspect.FacetDefinitions.Select(static facet => facet with { Id = string.Empty }).ToList(),
    };

    // A definition that pins an aspect version the target already holds embeds the target's copy, so its pins keep
    // matching the target's store exactly once imported.
    private static ResourceDefinition PinToTargetAspects(
        ResourceDefinition definition,
        IReadOnlyDictionary<(string AspectDefinitionId, int Version), AspectDefinition> targetAspects) =>
        definition.AspectDefinitions.Any(pair => pair.Value.IsShared && targetAspects.ContainsKey((pair.Value.AspectDefinitionId, pair.Value.Version)))
            ? definition with
            {
                AspectDefinitions = definition.AspectDefinitions.ToDictionary(
                    static pair => pair.Key,
                    pair => pair.Value.IsShared && targetAspects.TryGetValue((pair.Value.AspectDefinitionId, pair.Value.Version), out var stored)
                        ? stored with { IsShared = true }
                        : pair.Value,
                    StringComparer.Ordinal),
            }
            : definition;

    private static string AspectVersionId(AspectDefinition aspect) =>
        JsonSerializer.Serialize(new object[] { aspect.AspectDefinitionId, aspect.Version }, JsonOptions);

    private static string DefinitionVersionId(ResourceDefinition definition) =>
        JsonSerializer.Serialize(new object[] { definition.DefinitionId, definition.Version }, JsonOptions);

    private static string ResourceVersionId(Resource resource) =>
        JsonSerializer.Serialize(new object[] { resource.ResourceId, resource.Version }, JsonOptions);

    private static string ActivationEntryId(ActivationState state) =>
        JsonSerializer.Serialize(new object[] { state.ResourceId, state.Channel }, JsonOptions);

    private static string LifecycleMarkerId(ResourceLifecycleMarker marker) =>
        JsonSerializer.Serialize(new object[] { marker.ResourceId }, JsonOptions);

    /// <summary>
    /// Reference facets per resource, from the definition version the resource records (or the
    /// latest one in the set when it records none), cached per definition version.
    /// </summary>
    private sealed class SnapshotReferenceFacets(IEnumerable<ResourceDefinition> definitions)
    {
        private readonly Dictionary<(string DefinitionId, int Version), ResourceDefinition> byVersion =
            definitions.DistinctBy(static definition => (definition.DefinitionId, definition.Version))
                .ToDictionary(static definition => (definition.DefinitionId, definition.Version));

        private readonly Dictionary<(string DefinitionId, int Version), IReadOnlyList<ReferenceFacetValues.ReferenceFacet>> cache = [];

        public IReadOnlyList<ReferenceFacetValues.ReferenceFacet> For(Resource resource)
        {
            var definition = resource.DefinitionVersion is { } version && byVersion.TryGetValue((resource.DefinitionId, version), out var recorded)
                ? recorded
                : byVersion.Values
                    .Where(candidate => string.Equals(candidate.DefinitionId, resource.DefinitionId, StringComparison.Ordinal))
                    .MaxBy(static candidate => candidate.Version);
            if (definition is null)
                return [];

            var key = (definition.DefinitionId, definition.Version);
            if (!cache.TryGetValue(key, out var facets))
                cache[key] = facets = ReferenceFacetValues.Facets(definition);

            return facets;
        }
    }

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
            PlannedCounts.AspectDefinitions > 0
            || PlannedCounts.Definitions > 0
            || PlannedCounts.ResourceVersions > 0
            || PlannedCounts.ActivationEntries > 0
            || PlannedCounts.LifecycleMarkers > 0;

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
