using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
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
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);

        var resourceId = string.IsNullOrWhiteSpace(request.ResourceId)
            ? identityGenerator.NewId()
            : request.ResourceId;

        var existingVersions = (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
            TenantScope = tenant,
        }, cancellationToken)).ToList();

        if (existingVersions.Any(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal)))
            throw new DuplicateResourceIdException(resourceId);

        var definition = tenant.IsDefault
            ? await definitionStore.GetDefinitionAsync(definitionId, cancellationToken)
            : await definitionStore.GetDefinitionAsync(definitionId, tenant, cancellationToken);
        if (definition?.IsSingleton == true
            && existingVersions.Any(r => string.Equals(r.DefinitionId, definitionId, StringComparison.Ordinal)))
            throw new SingletonViolationException(definitionId);

        var resource = new Resource
        {
            TenantScope = tenant,
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
            TenantScope = tenant,
            SaveKind = ResourceSaveKind.Create,
            DefinitionId = definitionId,
            ResourceId = resourceId,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(resource),
        }, cancellationToken);

        var afterContext = new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterSave,
            CancellationToken = cancellationToken,
            TenantScope = tenant,
            SaveKind = ResourceSaveKind.Create,
            DefinitionId = definitionId,
            ResourceId = resourceId,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(resource),
        };

        Resource persisted;
        try
        {
            persisted = await versionWriter.SaveVersionAsync(resource, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterSaveAsync(afterContext, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterSaveAsync(afterContext with
        {
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
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);

        var versions = (await GetResourceVersionsAsync(resourceId, tenant, cancellationToken)).ToList();
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
            TenantScope = tenant,
            SaveKind = ResourceSaveKind.Update,
            DefinitionId = updated.DefinitionId,
            ResourceId = resourceId,
            BaseVersion = request.BaseVersion,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(updated),
        }, cancellationToken);

        var afterContext = new ResourceSaveLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterSave,
            CancellationToken = cancellationToken,
            TenantScope = tenant,
            SaveKind = ResourceSaveKind.Update,
            DefinitionId = updated.DefinitionId,
            ResourceId = updated.ResourceId,
            BaseVersion = request.BaseVersion,
            Resource = ResourceLifecycleHookContextSnapshots.Snapshot(updated),
        };

        Resource persisted;
        try
        {
            persisted = await versionWriter.SaveVersionAsync(updated, cancellationToken);
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

        LogResourceSaved(persisted.ResourceId, persisted.Version, persisted.Id);
        return persisted;
    }

    /// <inheritdoc />
    public async ValueTask<Resource?> GetVersionAsync(
        string resourceId,
        int version,
        CancellationToken cancellationToken = default) =>
        await GetVersionAsync(resourceId, version, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<Resource?> GetVersionAsync(
        string resourceId,
        int version,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        return (await GetResourceVersionsAsync(resourceId, tenant, cancellationToken))
            .FirstOrDefault(r => r.Version == version);
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> GetVersionsAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        await GetVersionsAsync(resourceId, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> GetVersionsAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        return await GetResourceVersionsAsync(resourceId, tenant, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Resource?> GetLatestVersionAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        await GetLatestVersionAsync(resourceId, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<Resource?> GetLatestVersionAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        return (await GetResourceVersionsAsync(resourceId, tenant, cancellationToken))
            .LastOrDefault();
    }

    /// <inheritdoc />
    public async ValueTask ActivateAsync(
        string resourceId,
        int version,
        string channel,
        bool allowMultipleActive = false,
        CancellationToken cancellationToken = default) =>
        await ActivateAsync(resourceId, version, channel, TenantScope.Default, allowMultipleActive, cancellationToken);

    /// <inheritdoc />
    public async ValueTask ActivateAsync(
        string resourceId,
        int version,
        string channel,
        TenantScope tenantScope,
        bool allowMultipleActive,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = (await GetResourceVersionsAsync(resourceId, tenant, cancellationToken)).ToList();
        if (versions.All(r => r.Version != version))
            throw new VersionNotFoundException(resourceId, version);

        var latest = versions[^1];
        if (version != latest.Version)
            throw new ConcurrencyException(resourceId, version, latest.Version);

        var activeVersions = allowMultipleActive
            ? (await GetActiveVersionsAsync(resourceId, channel, tenant, cancellationToken))
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
            TenantScope = tenant,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            AllowMultipleActive = allowMultipleActive,
            ActiveVersions = hookActiveVersions,
        }, cancellationToken);

        var afterContext = new ResourceActivationLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterActivate,
            CancellationToken = cancellationToken,
            TenantScope = tenant,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            AllowMultipleActive = allowMultipleActive,
            ActiveVersions = hookActiveVersions,
        };

        try
        {
            await WriteActivationStateAsync(resourceId, channel, tenant, resultingActiveVersions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterActivateAsync(afterContext, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterActivateAsync(afterContext, cancellationToken);

        LogResourceActivated(resourceId, version, channel);
    }

    /// <inheritdoc />
    public async ValueTask DeactivateAsync(
        string resourceId,
        int version,
        string channel,
        CancellationToken cancellationToken = default) =>
        await DeactivateAsync(resourceId, version, channel, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DeactivateAsync(
        string resourceId,
        int version,
        string channel,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var activeVersions = (await GetActiveVersionsAsync(resourceId, channel, tenant, cancellationToken))
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
            TenantScope = tenant,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            ActiveVersions = hookActiveVersions,
        }, cancellationToken);

        var afterContext = new ResourceActivationLifecycleContext
        {
            OperationId = operationId,
            LifecyclePoint = LifecyclePoint.AfterDeactivate,
            CancellationToken = cancellationToken,
            TenantScope = tenant,
            ResourceId = resourceId,
            Version = version,
            Channel = channel,
            ActiveVersions = hookActiveVersions,
        };

        try
        {
            await WriteActivationStateAsync(resourceId, channel, tenant, resultingActiveVersions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterDeactivateAsync(afterContext, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterDeactivateAsync(afterContext, cancellationToken);

        LogResourceDeactivated(resourceId, version, channel);
    }

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(
        string resourceId,
        string channel,
        CancellationToken cancellationToken = default) =>
        await GetActiveVersionsAsync(resourceId, channel, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(
        string resourceId,
        string channel,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var active = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = channel,
            TenantScope = tenant,
        }, cancellationToken);

        return active
            .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
            .OrderBy(r => r.Version)
            .ToList();
    }

    private async Task<IEnumerable<Resource>> GetResourceVersionsAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        var versions = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
            TenantScope = tenant,
        }, cancellationToken);

        return versions
            .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
            .OrderBy(r => r.Version)
            .ToList();
    }

    private async Task WriteActivationStateAsync(
        string resourceId,
        string channel,
        TenantScope tenantScope,
        IReadOnlyCollection<int> activeVersions,
        CancellationToken cancellationToken)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        var state = new ActivationState
        {
            TenantScope = tenant,
            ResourceId = resourceId,
            Channel = channel,
            ActiveVersions = activeVersions.Order().ToList(),
            LastUpdated = DateTime.UtcNow,
        };

        await versionWriter.UpdateActivationAsync(resourceId, channel, state, cancellationToken);
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

    private async ValueTask TryInvokeAfterActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleHooks.InvokeAfterActivateAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async ValueTask TryInvokeAfterDeactivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleHooks.InvokeAfterDeactivateAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
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
