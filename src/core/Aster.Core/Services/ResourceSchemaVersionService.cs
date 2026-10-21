using System.Collections.ObjectModel;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Default implementation of explicit resource schema-version status and upgrade operations.
/// </summary>
internal sealed class ResourceSchemaVersionService : IResourceSchemaVersionService
{
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceManager resourceManager;
    private readonly IIdentityGenerator identityGenerator;
    private readonly IResourceVersionWriter versionWriter;
    private readonly IResourceLifecycleHookDispatcher lifecycleHooks;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceSchemaVersionService"/> class.
    /// </summary>
    /// <param name="definitionStore">The resource definition version store.</param>
    /// <param name="resourceManager">The resource lifecycle manager.</param>
    /// <param name="identityGenerator">The identity generator used for new version IDs.</param>
    /// <param name="versionWriter">The resource version writer used to append upgraded versions.</param>
    public ResourceSchemaVersionService(
        IResourceDefinitionStore definitionStore,
        IResourceManager resourceManager,
        IIdentityGenerator identityGenerator,
        IResourceVersionWriter versionWriter)
        : this(
            definitionStore,
            resourceManager,
            identityGenerator,
            versionWriter,
            NoopResourceLifecycleHookDispatcher.Instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceSchemaVersionService"/> class.
    /// </summary>
    /// <param name="definitionStore">The resource definition version store.</param>
    /// <param name="resourceManager">The resource lifecycle manager.</param>
    /// <param name="identityGenerator">The identity generator used for new version IDs.</param>
    /// <param name="versionWriter">The resource version writer used to append upgraded versions.</param>
    /// <param name="lifecycleHooks">The lifecycle hook dispatcher.</param>
    public ResourceSchemaVersionService(
        IResourceDefinitionStore definitionStore,
        IResourceManager resourceManager,
        IIdentityGenerator identityGenerator,
        IResourceVersionWriter versionWriter,
        IResourceLifecycleHookDispatcher lifecycleHooks)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(resourceManager);
        ArgumentNullException.ThrowIfNull(identityGenerator);
        ArgumentNullException.ThrowIfNull(versionWriter);
        ArgumentNullException.ThrowIfNull(lifecycleHooks);

        this.definitionStore = definitionStore;
        this.resourceManager = resourceManager;
        this.identityGenerator = identityGenerator;
        this.versionWriter = versionWriter;
        this.lifecycleHooks = lifecycleHooks;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceSchemaStatusResult> GetSchemaStatusAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var tenant = TenantScopeResolver.Resolve(resource.TenantScope);

        var latestDefinition = await definitionStore.GetDefinitionAsync(resource.DefinitionId, tenant, cancellationToken);
        if (latestDefinition is null)
            return Status(resource, ResourceSchemaStatus.MissingDefinition, latestDefinitionVersion: null,
                $"Definition '{resource.DefinitionId}' was not found.");

        if (resource.DefinitionVersion is null)
            return Status(resource, ResourceSchemaStatus.UnknownResourceLineage, latestDefinition.Version,
                $"Resource version {resource.Version} does not record definition version lineage.");

        var recordedDefinition = await definitionStore.GetDefinitionVersionAsync(
            resource.DefinitionId,
            resource.DefinitionVersion.Value,
            tenant,
            cancellationToken);
        if (recordedDefinition is null)
            return Status(resource, ResourceSchemaStatus.MissingDefinitionVersion, latestDefinition.Version,
                $"Definition '{resource.DefinitionId}' version {resource.DefinitionVersion.Value} was not found.");

        if (resource.DefinitionVersion.Value < latestDefinition.Version)
            return Status(resource, ResourceSchemaStatus.OlderThanLatest, latestDefinition.Version,
                $"Resource version {resource.Version} uses definition version {resource.DefinitionVersion.Value}; latest is {latestDefinition.Version}.");

        if (resource.DefinitionVersion.Value == latestDefinition.Version)
            return Status(resource, ResourceSchemaStatus.Current, latestDefinition.Version,
                $"Resource version {resource.Version} uses the latest definition version {latestDefinition.Version}.");

        return Status(resource, ResourceSchemaStatus.MissingDefinitionVersion, latestDefinition.Version,
            $"Definition '{resource.DefinitionId}' version {resource.DefinitionVersion.Value} is newer than the latest known version {latestDefinition.Version}.");
    }

    /// <inheritdoc />
    public async ValueTask<ResourceSchemaUpgradeResult> UpgradeAsync(
        string resourceId,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(request);

        var plan = await PlanAsync(resourceId, request, cancellationToken);
        switch (plan.Status)
        {
            case ResourceSchemaUpgradeStatus.Rejected:
                return Rejected(resourceId, plan, plan.Diagnostics);

            case ResourceSchemaUpgradeStatus.NoOp:
                return new ResourceSchemaUpgradeResult
                {
                    Status = ResourceSchemaUpgradeStatus.NoOp,
                    Resource = plan.Latest,
                    SourceDefinitionVersion = plan.Latest!.DefinitionVersion,
                    TargetDefinitionVersion = plan.TargetVersion!.Value,
                    CarriedForwardAspectKeys = [],
                    Message = plan.Message,
                };
        }

        Resource upgraded;
        try
        {
            upgraded = await SaveUpgradedResourceAsync(plan.Latest!, plan.TargetVersion!.Value, plan.MergedAspects!, cancellationToken);
        }
        catch (ConcurrencyException exception)
        {
            // Another writer appended this version between the base-version check and the write.
            return Rejected(resourceId, plan, [Diagnostic(ResourceSchemaUpgradeDiagnosticCodes.ConcurrencyConflict, exception.Message)]);
        }

        return new ResourceSchemaUpgradeResult
        {
            Status = ResourceSchemaUpgradeStatus.Upgraded,
            Resource = upgraded,
            SourceDefinitionVersion = plan.Latest!.DefinitionVersion,
            TargetDefinitionVersion = plan.TargetVersion!.Value,
            CarriedForwardAspectKeys = plan.CarriedForwardAspectKeys,
            Message = $"Resource '{resourceId}' upgraded to definition version {plan.TargetVersion}.",
        };
    }

    /// <inheritdoc />
    public async ValueTask<ResourceSchemaUpgradePreview> UpgradePreviewAsync(
        string resourceId,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(request);

        var plan = await PlanAsync(resourceId, request, cancellationToken);
        return new ResourceSchemaUpgradePreview
        {
            ResourceId = resourceId,
            ResourceVersion = plan.Latest?.Version,
            DefinitionId = plan.Latest?.DefinitionId,
            Status = plan.Status,
            SourceDefinitionVersion = plan.Latest?.DefinitionVersion,
            TargetDefinitionVersion = plan.TargetVersion,
            CarriedForwardAspectKeys = plan.CarriedForwardAspectKeys,
            Diagnostics = plan.Diagnostics,
            Message = plan.Message,
        };
    }

    /// <summary>
    /// Works out what an upgrade request would do, shared by the preview and the upgrade so they cannot disagree.
    /// Reads only; every expected failure becomes a diagnostic. A stale base version is reported alongside the
    /// other checks, which run against the latest version, so a preview lists every blocking reason at once.
    /// </summary>
    private async ValueTask<UpgradePlan> PlanAsync(
        string resourceId,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);
        var diagnostics = new List<ResourceSchemaUpgradeDiagnostic>();

        var latest = await resourceManager.GetLatestVersionAsync(resourceId, tenant, cancellationToken);
        if (latest is null)
        {
            diagnostics.Add(Diagnostic(ResourceSchemaUpgradeDiagnosticCodes.ResourceNotFound, $"Resource '{resourceId}' was not found."));
            return Reject(resourceId, latest: null, request.TargetDefinitionVersion, diagnostics);
        }

        if (latest.Version != request.BaseVersion)
            diagnostics.Add(Diagnostic(
                ResourceSchemaUpgradeDiagnosticCodes.ConcurrencyConflict,
                $"Concurrency conflict on resource '{resourceId}': expected version {request.BaseVersion}, but found version {latest.Version}."));

        var latestDefinition = await definitionStore.GetDefinitionAsync(latest.DefinitionId, tenant, cancellationToken);
        if (latestDefinition is null)
        {
            diagnostics.Add(Diagnostic(ResourceSchemaUpgradeDiagnosticCodes.MissingDefinition, $"Definition '{latest.DefinitionId}' was not found."));
            return Reject(resourceId, latest, request.TargetDefinitionVersion, diagnostics);
        }

        var targetVersion = request.TargetDefinitionVersion ?? latestDefinition.Version;
        if (targetVersion > latestDefinition.Version)
        {
            diagnostics.Add(Diagnostic(
                ResourceSchemaUpgradeDiagnosticCodes.TargetDefinitionVersionTooNew,
                $"Definition '{latest.DefinitionId}' version {targetVersion} is newer than the latest known version {latestDefinition.Version}."));
            return Reject(resourceId, latest, targetVersion, diagnostics);
        }

        var targetDefinition = await definitionStore.GetDefinitionVersionAsync(latest.DefinitionId, targetVersion, tenant, cancellationToken);
        if (targetDefinition is null)
        {
            diagnostics.Add(Diagnostic(
                ResourceSchemaUpgradeDiagnosticCodes.MissingDefinitionVersion,
                $"Definition '{latest.DefinitionId}' version {targetVersion} was not found."));
            return Reject(resourceId, latest, targetVersion, diagnostics);
        }

        if (latest.DefinitionVersion == targetVersion)
        {
            return diagnostics.Count > 0
                ? Reject(resourceId, latest, targetVersion, diagnostics)
                : new UpgradePlan(
                    ResourceSchemaUpgradeStatus.NoOp,
                    latest,
                    targetVersion,
                    MergedAspects: null,
                    CarriedForwardAspectKeys: [],
                    Diagnostics: [],
                    $"Resource '{resourceId}' already uses definition version {targetVersion}.");
        }

        if (latest.DefinitionVersion is { } sourceVersion && targetVersion < sourceVersion)
        {
            diagnostics.Add(Diagnostic(
                ResourceSchemaUpgradeDiagnosticCodes.TargetDefinitionVersionBeforeSource,
                $"Definition '{latest.DefinitionId}' version {targetVersion} is older than source version {sourceVersion}."));
            return Reject(resourceId, latest, targetVersion, diagnostics);
        }

        var mergedAspects = new Dictionary<string, object>(latest.Aspects, StringComparer.Ordinal);
        foreach (var (key, value) in request.AspectUpdates)
            mergedAspects[key] = value;

        // The new version is recorded against the target definition, so its payload must satisfy it, as a normal
        // update's does. Undeclared, carried-forward aspects are not validated.
        diagnostics.AddRange(FacetValueValidator.Validate(targetDefinition, mergedAspects).Select(static diagnostic =>
            new ResourceSchemaUpgradeDiagnostic
            {
                Code = diagnostic.Code,
                Message = diagnostic.Message,
                AspectKey = diagnostic.AspectKey,
                FacetDefinitionId = diagnostic.FacetDefinitionId,
                ElementIndex = diagnostic.ElementIndex,
            }));

        var carriedForwardAspectKeys = new ReadOnlyCollection<string>(
            GetCarriedForwardAspectKeys(latest, targetDefinition, request.AspectUpdates));

        if (diagnostics.Count > 0)
            return Reject(resourceId, latest, targetVersion, diagnostics) with { CarriedForwardAspectKeys = carriedForwardAspectKeys };

        return new UpgradePlan(
            ResourceSchemaUpgradeStatus.Upgraded,
            latest,
            targetVersion,
            mergedAspects,
            carriedForwardAspectKeys,
            Diagnostics: [],
            $"Resource '{resourceId}' would be upgraded from definition version {latest.DefinitionVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} to {targetVersion}.");
    }

    private static UpgradePlan Reject(
        string resourceId,
        Resource? latest,
        int? targetVersion,
        IReadOnlyList<ResourceSchemaUpgradeDiagnostic> diagnostics) =>
        new(
            ResourceSchemaUpgradeStatus.Rejected,
            latest,
            targetVersion,
            MergedAspects: null,
            CarriedForwardAspectKeys: [],
            new ReadOnlyCollection<ResourceSchemaUpgradeDiagnostic>([.. diagnostics]),
            RejectionMessage(resourceId, diagnostics));

    private static ResourceSchemaUpgradeResult Rejected(
        string resourceId,
        UpgradePlan plan,
        IReadOnlyList<ResourceSchemaUpgradeDiagnostic> diagnostics) =>
        new()
        {
            Status = ResourceSchemaUpgradeStatus.Rejected,
            SourceDefinitionVersion = plan.Latest?.DefinitionVersion,
            TargetDefinitionVersion = plan.TargetVersion ?? 0,
            CarriedForwardAspectKeys = [],
            Diagnostics = diagnostics,
            Message = RejectionMessage(resourceId, diagnostics),
        };

    private static string RejectionMessage(string resourceId, IReadOnlyList<ResourceSchemaUpgradeDiagnostic> diagnostics) =>
        diagnostics.Count == 1
            ? diagnostics[0].Message
            : $"Resource '{resourceId}' cannot be upgraded ({diagnostics.Count} diagnostics). {diagnostics[0].Message}";

    private static ResourceSchemaUpgradeDiagnostic Diagnostic(string code, string message) =>
        new() { Code = code, Message = message };

    /// <summary>The outcome an upgrade request would have, with what the write needs when it would upgrade.</summary>
    private sealed record UpgradePlan(
        ResourceSchemaUpgradeStatus Status,
        Resource? Latest,
        int? TargetVersion,
        Dictionary<string, object>? MergedAspects,
        IReadOnlyList<string> CarriedForwardAspectKeys,
        IReadOnlyList<ResourceSchemaUpgradeDiagnostic> Diagnostics,
        string Message);

    private async ValueTask<Resource> SaveUpgradedResourceAsync(
        Resource latestResource,
        int targetVersion,
        Dictionary<string, object> mergedAspects,
        CancellationToken cancellationToken)
    {
        var upgraded = latestResource with
        {
            Id = identityGenerator.NewId(),
            Version = latestResource.Version + 1,
            Created = DateTime.UtcNow,
            DefinitionVersion = targetVersion,
            Aspects = mergedAspects,
        };

        var operationId = Guid.NewGuid();
        await lifecycleHooks.InvokeBeforeSaveAsync(new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.BeforeSave,
            CancellationToken = cancellationToken,
            TenantScope = latestResource.TenantScope,
            SaveKind = ResourceSaveKind.SchemaUpgrade,
            DefinitionId = upgraded.DefinitionId,
            ResourceId = upgraded.ResourceId,
            BaseVersion = latestResource.Version,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(upgraded),
        }, cancellationToken);

        var afterContext = new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterSave,
            CancellationToken = cancellationToken,
            TenantScope = latestResource.TenantScope,
            SaveKind = ResourceSaveKind.SchemaUpgrade,
            DefinitionId = upgraded.DefinitionId,
            ResourceId = upgraded.ResourceId,
            BaseVersion = latestResource.Version,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(upgraded),
        };

        Resource persisted;
        try
        {
            persisted = await versionWriter.SaveVersionAsync(upgraded, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterSaveAsync(afterContext, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterSaveAsync(afterContext with
        {
            DefinitionId = persisted.DefinitionId,
            ResourceId = persisted.ResourceId,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(persisted),
        }, cancellationToken);

        return persisted;
    }

    private async ValueTask TryInvokeAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleHooks.InvokeAfterSaveAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private static List<string> GetCarriedForwardAspectKeys(
        Resource resource,
        ResourceDefinition targetDefinition,
        Dictionary<string, object> explicitUpdates)
    {
        var declared = targetDefinition.AspectDefinitions.Keys.ToHashSet(StringComparer.Ordinal);
        var explicitlyChanged = explicitUpdates.Keys.ToHashSet(StringComparer.Ordinal);

        return resource.Aspects.Keys
            .Where(key => !declared.Contains(key) && !explicitlyChanged.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static ResourceSchemaStatusResult Status(
        Resource resource,
        ResourceSchemaStatus status,
        int? latestDefinitionVersion,
        string message) =>
        new()
        {
            ResourceId = resource.ResourceId,
            ResourceVersion = resource.Version,
            DefinitionId = resource.DefinitionId,
            RecordedDefinitionVersion = resource.DefinitionVersion,
            LatestDefinitionVersion = latestDefinitionVersion,
            Status = status,
            Message = message,
        };
}
