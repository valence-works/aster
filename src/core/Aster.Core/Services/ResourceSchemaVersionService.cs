using System.Collections.ObjectModel;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;

namespace Aster.Core.Services;

/// <summary>
/// Default implementation of explicit resource schema-version status and upgrade operations.
/// </summary>
public sealed class ResourceSchemaVersionService : IResourceSchemaVersionService
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

        var latestDefinition = await definitionStore.GetDefinitionAsync(resource.DefinitionId, cancellationToken);
        if (latestDefinition is null)
            return Status(resource, ResourceSchemaStatus.MissingDefinition, latestDefinitionVersion: null,
                $"Definition '{resource.DefinitionId}' was not found.");

        if (resource.DefinitionVersion is null)
            return Status(resource, ResourceSchemaStatus.UnknownResourceLineage, latestDefinition.Version,
                $"Resource version {resource.Version} does not record definition version lineage.");

        var recordedDefinition = await definitionStore.GetDefinitionVersionAsync(
            resource.DefinitionId,
            resource.DefinitionVersion.Value,
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

        var latestResource = await resourceManager.GetLatestVersionAsync(resourceId, cancellationToken)
            ?? throw new VersionNotFoundException(resourceId, request.BaseVersion);

        if (latestResource.Version != request.BaseVersion)
            throw new ConcurrencyException(resourceId, request.BaseVersion, latestResource.Version);

        var latestDefinition = await definitionStore.GetDefinitionAsync(latestResource.DefinitionId, cancellationToken)
            ?? throw UpgradeFailure(
                "missing-definition",
                $"Definition '{latestResource.DefinitionId}' was not found.",
                latestResource.DefinitionId,
                request.TargetDefinitionVersion);

        var targetVersion = request.TargetDefinitionVersion ?? latestDefinition.Version;
        if (targetVersion > latestDefinition.Version)
            throw UpgradeFailure(
                "target-definition-version-too-new",
                $"Definition '{latestResource.DefinitionId}' version {targetVersion} is newer than the latest known version {latestDefinition.Version}.",
                latestResource.DefinitionId,
                targetVersion);

        var targetDefinition = await definitionStore.GetDefinitionVersionAsync(
            latestResource.DefinitionId,
            targetVersion,
            cancellationToken);
        if (targetDefinition is null)
            throw UpgradeFailure(
                "missing-definition-version",
                $"Definition '{latestResource.DefinitionId}' version {targetVersion} was not found.",
                latestResource.DefinitionId,
                targetVersion);

        if (latestResource.DefinitionVersion == targetVersion)
            return new ResourceSchemaUpgradeResult
            {
                Status = ResourceSchemaUpgradeStatus.NoOp,
                Resource = latestResource,
                SourceDefinitionVersion = latestResource.DefinitionVersion,
                TargetDefinitionVersion = targetVersion,
                CarriedForwardAspectKeys = [],
                Message = $"Resource '{resourceId}' already uses definition version {targetVersion}.",
            };

        if (latestResource.DefinitionVersion is not null
            && targetVersion < latestResource.DefinitionVersion.Value)
        {
            throw UpgradeFailure(
                "target-definition-version-before-source",
                $"Definition '{latestResource.DefinitionId}' version {targetVersion} is older than source version {latestResource.DefinitionVersion.Value}.",
                latestResource.DefinitionId,
                targetVersion);
        }

        var upgraded = await SaveUpgradedResourceAsync(latestResource, targetVersion, request, cancellationToken);
        var carriedForwardAspectKeys = GetCarriedForwardAspectKeys(latestResource, targetDefinition, request.AspectUpdates);

        return new ResourceSchemaUpgradeResult
        {
            Status = ResourceSchemaUpgradeStatus.Upgraded,
            Resource = upgraded,
            SourceDefinitionVersion = latestResource.DefinitionVersion,
            TargetDefinitionVersion = targetVersion,
            CarriedForwardAspectKeys = new ReadOnlyCollection<string>(carriedForwardAspectKeys),
            Message = $"Resource '{resourceId}' upgraded to definition version {targetVersion}.",
        };
    }

    private async ValueTask<Resource> SaveUpgradedResourceAsync(
        Resource latestResource,
        int targetVersion,
        ResourceSchemaUpgradeRequest request,
        CancellationToken cancellationToken)
    {
        var mergedAspects = new Dictionary<string, object>(latestResource.Aspects, StringComparer.Ordinal);
        foreach (var (key, value) in request.AspectUpdates)
            mergedAspects[key] = value;

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
            SaveKind = ResourceSaveKind.SchemaUpgrade,
            DefinitionId = upgraded.DefinitionId,
            ResourceId = upgraded.ResourceId,
            BaseVersion = latestResource.Version,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(upgraded),
        }, cancellationToken);

        var persisted = await versionWriter.SaveVersionAsync(upgraded, cancellationToken);
        await lifecycleHooks.InvokeAfterSaveAsync(new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterSave,
            CancellationToken = cancellationToken,
            SaveKind = ResourceSaveKind.SchemaUpgrade,
            DefinitionId = persisted.DefinitionId,
            ResourceId = persisted.ResourceId,
            BaseVersion = latestResource.Version,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(persisted),
        }, cancellationToken);

        return persisted;
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

    private static ResourceSchemaUpgradeException UpgradeFailure(
        string code,
        string message,
        string definitionId,
        int? targetDefinitionVersion) =>
        new(code, message, definitionId, targetDefinitionVersion);
}
