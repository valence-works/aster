using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Querying;
using Microsoft.Extensions.Logging;

namespace Aster.Core.Services;

/// <summary>
/// Provider-backed implementation of <see cref="IResourceManager"/>.
/// Orchestrates lifecycle behavior using resource version reader/writer primitives.
/// </summary>
public sealed partial class DefaultResourceManager : IResourceManager
{
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceVersionWriter versionWriter;
    private readonly IIdentityGenerator identityGenerator;
    private readonly IResourceLifecycleHookDispatcher lifecycleHooks;
    private readonly ILogger<DefaultResourceManager> logger;

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultResourceManager"/>.
    /// </summary>
    public DefaultResourceManager(
        IResourceDefinitionStore definitionStore,
        IResourceVersionReader versionReader,
        IResourceVersionWriter versionWriter,
        IIdentityGenerator identityGenerator,
        ILogger<DefaultResourceManager> logger)
        : this(
            definitionStore,
            versionReader,
            versionWriter,
            identityGenerator,
            NoopResourceLifecycleHookDispatcher.Instance,
            logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="DefaultResourceManager"/>.
    /// </summary>
    public DefaultResourceManager(
        IResourceDefinitionStore definitionStore,
        IResourceVersionReader versionReader,
        IResourceVersionWriter versionWriter,
        IIdentityGenerator identityGenerator,
        IResourceLifecycleHookDispatcher lifecycleHooks,
        ILogger<DefaultResourceManager> logger)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(versionWriter);
        ArgumentNullException.ThrowIfNull(identityGenerator);
        ArgumentNullException.ThrowIfNull(lifecycleHooks);
        ArgumentNullException.ThrowIfNull(logger);

        this.definitionStore = definitionStore;
        this.versionReader = versionReader;
        this.versionWriter = versionWriter;
        this.identityGenerator = identityGenerator;
        this.lifecycleHooks = lifecycleHooks;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<Resource> CreateAsync(
        string definitionId,
        CreateResourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(request);

        var resourceId = string.IsNullOrWhiteSpace(request.ResourceId)
            ? identityGenerator.NewId()
            : request.ResourceId;

        var existingVersions = (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
        }, cancellationToken)).ToList();

        if (existingVersions.Any(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal)))
            throw new DuplicateResourceIdException(resourceId);

        var definition = await definitionStore.GetDefinitionAsync(definitionId, cancellationToken);
        if (definition?.IsSingleton == true
            && existingVersions.Any(r => string.Equals(r.DefinitionId, definitionId, StringComparison.Ordinal)))
            throw new SingletonViolationException(definitionId);

        var resource = new Resource
        {
            ResourceId = resourceId,
            Id = identityGenerator.NewId(),
            DefinitionId = definitionId,
            DefinitionVersion = definition?.Version,
            Version = 1,
            Created = DateTime.UtcNow,
            Aspects = new Dictionary<string, object>(request.InitialAspects),
        };

        var operationId = Guid.NewGuid();
        await lifecycleHooks.InvokeBeforeSaveAsync(new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.BeforeSave,
            CancellationToken = cancellationToken,
            SaveKind = ResourceSaveKind.Create,
            DefinitionId = definitionId,
            ResourceId = resourceId,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(resource),
        }, cancellationToken);

        var persisted = await versionWriter.SaveVersionAsync(resource, cancellationToken);
        await lifecycleHooks.InvokeAfterSaveAsync(new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterSave,
            CancellationToken = cancellationToken,
            SaveKind = ResourceSaveKind.Create,
            DefinitionId = definitionId,
            ResourceId = persisted.ResourceId,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(persisted),
        }, cancellationToken);

        LogResourceSaved(persisted.ResourceId, persisted.Version, persisted.Id);
        return persisted;
    }

    /// <inheritdoc />
    public async ValueTask<Resource> UpdateAsync(
        string resourceId,
        UpdateResourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(request);

        var versions = (await GetResourceVersionsAsync(resourceId, cancellationToken)).ToList();
        var latest = versions.LastOrDefault()
            ?? throw new VersionNotFoundException(resourceId, request.BaseVersion);

        if (latest.Version != request.BaseVersion)
            throw new ConcurrencyException(resourceId, request.BaseVersion, latest.Version);

        var mergedAspects = new Dictionary<string, object>(latest.Aspects, StringComparer.Ordinal);
        foreach (var (key, value) in request.AspectUpdates)
            mergedAspects[key] = value;

        var updated = latest with
        {
            Id = identityGenerator.NewId(),
            Version = latest.Version + 1,
            Created = DateTime.UtcNow,
            Aspects = mergedAspects,
        };

        var operationId = Guid.NewGuid();
        await lifecycleHooks.InvokeBeforeSaveAsync(new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.BeforeSave,
            CancellationToken = cancellationToken,
            SaveKind = ResourceSaveKind.Update,
            DefinitionId = updated.DefinitionId,
            ResourceId = resourceId,
            BaseVersion = request.BaseVersion,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(updated),
        }, cancellationToken);

        var persisted = await versionWriter.SaveVersionAsync(updated, cancellationToken);
        await lifecycleHooks.InvokeAfterSaveAsync(new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterSave,
            CancellationToken = cancellationToken,
            SaveKind = ResourceSaveKind.Update,
            DefinitionId = persisted.DefinitionId,
            ResourceId = persisted.ResourceId,
            BaseVersion = request.BaseVersion,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(persisted),
        }, cancellationToken);

        LogResourceSaved(persisted.ResourceId, persisted.Version, persisted.Id);
        return persisted;
    }

    /// <inheritdoc />
    public async ValueTask<Resource?> GetVersionAsync(
        string resourceId,
        int version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return (await GetResourceVersionsAsync(resourceId, cancellationToken))
            .FirstOrDefault(r => r.Version == version);
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> GetVersionsAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return await GetResourceVersionsAsync(resourceId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Resource?> GetLatestVersionAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return (await GetResourceVersionsAsync(resourceId, cancellationToken))
            .LastOrDefault();
    }

    /// <inheritdoc />
    public async ValueTask ActivateAsync(
        string resourceId,
        int version,
        string channel,
        bool allowMultipleActive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var versions = (await GetResourceVersionsAsync(resourceId, cancellationToken)).ToList();
        if (versions.All(r => r.Version != version))
            throw new VersionNotFoundException(resourceId, version);

        var latest = versions[^1];
        if (version != latest.Version)
            throw new ConcurrencyException(resourceId, version, latest.Version);

        var activeVersions = allowMultipleActive
            ? (await GetActiveVersionsAsync(resourceId, channel, cancellationToken))
                .Select(r => r.Version)
                .ToHashSet()
            : [];

        activeVersions.Add(version);
        var resultingActiveVersions = activeVersions.Order().ToList();
        var hookActiveVersions = ResourceLifecycleHookContextSnapshots.Snapshot(resultingActiveVersions);
        var operationId = Guid.NewGuid();
        await lifecycleHooks.InvokeBeforeActivateAsync(new ResourceActivationLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.BeforeActivate,
            CancellationToken = cancellationToken,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            AllowMultipleActive = allowMultipleActive,
            ActiveVersions = hookActiveVersions,
        }, cancellationToken);

        await WriteActivationStateAsync(resourceId, channel, resultingActiveVersions, cancellationToken);
        await lifecycleHooks.InvokeAfterActivateAsync(new ResourceActivationLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterActivate,
            CancellationToken = cancellationToken,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            AllowMultipleActive = allowMultipleActive,
            ActiveVersions = hookActiveVersions,
        }, cancellationToken);

        LogResourceActivated(resourceId, version, channel);
    }

    /// <inheritdoc />
    public async ValueTask DeactivateAsync(
        string resourceId,
        int version,
        string channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var activeVersions = (await GetActiveVersionsAsync(resourceId, channel, cancellationToken))
            .Select(r => r.Version)
            .ToHashSet();

        activeVersions.Remove(version);
        var resultingActiveVersions = activeVersions.Order().ToList();
        var hookActiveVersions = ResourceLifecycleHookContextSnapshots.Snapshot(resultingActiveVersions);
        var operationId = Guid.NewGuid();
        await lifecycleHooks.InvokeBeforeDeactivateAsync(new ResourceActivationLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.BeforeDeactivate,
            CancellationToken = cancellationToken,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            ActiveVersions = hookActiveVersions,
        }, cancellationToken);

        await WriteActivationStateAsync(resourceId, channel, resultingActiveVersions, cancellationToken);
        await lifecycleHooks.InvokeAfterDeactivateAsync(new ResourceActivationLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterDeactivate,
            CancellationToken = cancellationToken,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            ActiveVersions = hookActiveVersions,
        }, cancellationToken);

        LogResourceDeactivated(resourceId, version, channel);
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(
        string resourceId,
        string channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var active = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = channel,
        }, cancellationToken);

        return active
            .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
            .OrderBy(r => r.Version)
            .ToList();
    }

    private async Task<IEnumerable<Resource>> GetResourceVersionsAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        var versions = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
        }, cancellationToken);

        return versions
            .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
            .OrderBy(r => r.Version)
            .ToList();
    }

    private async Task WriteActivationStateAsync(
        string resourceId,
        string channel,
        IReadOnlyCollection<int> activeVersions,
        CancellationToken cancellationToken)
    {
        var state = new ActivationState
        {
            ResourceId = resourceId,
            Channel = channel,
            ActiveVersions = activeVersions.Order().ToList(),
            LastUpdated = DateTime.UtcNow,
        };

        await versionWriter.UpdateActivationAsync(resourceId, channel, state, cancellationToken);
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Saved resource '{ResourceId}' version {Version} (Id={Id}).")]
    private partial void LogResourceSaved(string resourceId, int version, string id);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Activated resource '{ResourceId}' version {Version} in channel '{Channel}'.")]
    private partial void LogResourceActivated(string resourceId, int version, string channel);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Deactivated resource '{ResourceId}' version {Version} in channel '{Channel}'.")]
    private partial void LogResourceDeactivated(string resourceId, int version, string channel);
}
