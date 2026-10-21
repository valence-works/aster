using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using Microsoft.Extensions.Logging;

namespace Aster.Core.Services;

/// <summary>
/// Provider-backed implementation of <see cref="IResourceManager"/>.
/// Orchestrates lifecycle behavior using resource version reader/writer primitives.
/// </summary>
internal sealed partial class DefaultResourceManager : IResourceManager
{
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IResourceVersionReader versionReader;
    private readonly IResourceVersionWriter versionWriter;
    private readonly IIdentityGenerator identityGenerator;
    private readonly IResourceLifecycleHookDispatcher lifecycleHooks;
    private readonly IResourceReferenceValidator referenceValidator;
    private readonly ReferenceValidationMode referenceValidation;
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
        : this(
            definitionStore,
            versionReader,
            versionWriter,
            identityGenerator,
            lifecycleHooks,
            new AsterOptions(),
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
        AsterOptions options,
        ILogger<DefaultResourceManager> logger)
    {
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(versionWriter);
        ArgumentNullException.ThrowIfNull(identityGenerator);
        ArgumentNullException.ThrowIfNull(lifecycleHooks);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.definitionStore = definitionStore;
        this.versionReader = versionReader;
        this.versionWriter = versionWriter;
        this.identityGenerator = identityGenerator;
        this.lifecycleHooks = lifecycleHooks;
        referenceValidator = new ResourceReferenceValidator(versionReader);
        referenceValidation = options.ReferenceValidation;
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

        // Bounded to this resource ID (#154): reading the whole tenant made every create O(tenant size).
        var existingVersions = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Latest,
            TenantScope = tenant,
            ResourceIds = [resourceId],
        }, cancellationToken);

        if (existingVersions.Any())
            throw new DuplicateResourceIdException(resourceId);

        var definition = await definitionStore.GetDefinitionAsync(definitionId, tenant, cancellationToken);
        if (definition?.IsSingleton == true && await HasResourceOfDefinitionAsync(definitionId, tenant, cancellationToken))
            throw new SingletonViolationException(definitionId);

        if (definition is not null)
        {
            var facetValueDiagnostics = FacetValueValidator.Validate(definition, request.InitialAspects);
            if (facetValueDiagnostics.Count > 0)
                throw new FacetValueValidationException(facetValueDiagnostics);
        }

        var referenceWarnings = await ValidateReferencesAsync(definition, request.InitialAspects, resourceId, tenant, cancellationToken);

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
            ReferenceWarnings = referenceWarnings,
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
            ReferenceWarnings = referenceWarnings,
        };

        Resource persisted;
        try
        {
            persisted = await versionWriter.SaveVersionAsync(resource, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterSaveAsync(afterContext, exception, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterSaveAsync(afterContext with
        {
            OperationStatus = LifecycleOperationStatus.Succeeded,
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

        var latest = await ReadLatestVersionAsync(resourceId, tenant, cancellationToken)
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

        var definition = await RecordedDefinitionResolver.ResolveAsync(definitionStore, updated, tenant, cancellationToken);
        if (definition is not null)
        {
            var facetValueDiagnostics = FacetValueValidator.Validate(definition, mergedAspects);
            if (facetValueDiagnostics.Count > 0)
                throw new FacetValueValidationException(facetValueDiagnostics);
        }

        var referenceWarnings = await ValidateReferencesAsync(
            definition, UpdatedAspects(mergedAspects, request), resourceId, tenant, cancellationToken);

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
            ReferenceWarnings = referenceWarnings,
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
            ReferenceWarnings = referenceWarnings,
        };

        Resource persisted;
        try
        {
            persisted = await versionWriter.SaveVersionAsync(updated, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await TryInvokeAfterSaveAsync(afterContext, exception, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterSaveAsync(afterContext with
        {
            OperationStatus = LifecycleOperationStatus.Succeeded,
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

        return await ReadVersionAsync(resourceId, version, tenant, cancellationToken);
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
        CancellationToken cancellationToken)
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
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        return await ReadLatestVersionAsync(resourceId, tenant, cancellationToken);
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

        var target = await ReadVersionAsync(resourceId, version, tenant, cancellationToken)
            ?? throw new VersionNotFoundException(resourceId, version);

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
            DefinitionId = target.DefinitionId,
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
            DefinitionId = target.DefinitionId,
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
            await TryInvokeAfterActivateAsync(afterContext, exception, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterActivateAsync(
            afterContext with { OperationStatus = LifecycleOperationStatus.Succeeded },
            cancellationToken);

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

        var definitionId = (await ReadLatestVersionAsync(resourceId, tenant, cancellationToken))?.DefinitionId;
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
            DefinitionId = definitionId,
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
            DefinitionId = definitionId,
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
            await TryInvokeAfterDeactivateAsync(afterContext, exception, cancellationToken);
            throw;
        }

        await lifecycleHooks.InvokeAfterDeactivateAsync(
            afterContext with { OperationStatus = LifecycleOperationStatus.Succeeded },
            cancellationToken);

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
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var active = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = channel,
            TenantScope = tenant,
            ResourceIds = [resourceId],
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
            ResourceIds = [resourceId],
        }, cancellationToken);

        return versions
            .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
            .OrderBy(r => r.Version)
            .ToList();
    }

    // Single-version reads address one row instead of loading the resource's history (#166).
    private async Task<Resource?> ReadLatestVersionAsync(
        string resourceId,
        TenantScope tenant,
        CancellationToken cancellationToken) =>
        (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Latest,
            TenantScope = tenant,
            ResourceIds = [resourceId],
        }, cancellationToken))
            .Where(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal))
            .MaxBy(static r => r.Version);

    private async Task<Resource?> ReadVersionAsync(
        string resourceId,
        int version,
        TenantScope tenant,
        CancellationToken cancellationToken) =>
        (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
            TenantScope = tenant,
            ResourceIds = [resourceId],
            Version = version,
        }, cancellationToken))
            .FirstOrDefault(r => string.Equals(r.ResourceId, resourceId, StringComparison.Ordinal) && r.Version == version);

    // Singleton definitions are the only create path that needs every resource of a definition; the
    // reader has no definition filter, so this reads latest versions only and only for singletons.
    private async Task<bool> HasResourceOfDefinitionAsync(
        string definitionId,
        TenantScope tenant,
        CancellationToken cancellationToken) =>
        (await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Latest,
            TenantScope = tenant,
        }, cancellationToken)).Any(r => string.Equals(r.DefinitionId, definitionId, StringComparison.Ordinal));

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

    // A failed write still runs the after hooks so before/after calls stay paired, with a Failed outcome so no hook
    // mistakes it for a completed change (#219). A hook failure here is dropped: the write's exception is the one the
    // caller receives.
    private async ValueTask TryInvokeAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        Exception writeFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleHooks.InvokeAfterSaveAsync(context with
            {
                OperationStatus = LifecycleOperationStatus.Failed,
                OperationFailure = LifecycleOperationFailures.FromException(writeFailure),
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async ValueTask TryInvokeAfterActivateAsync(
        ResourceActivationLifecycleContext context,
        Exception writeFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleHooks.InvokeAfterActivateAsync(context with
            {
                OperationStatus = LifecycleOperationStatus.Failed,
                OperationFailure = LifecycleOperationFailures.FromException(writeFailure),
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private async ValueTask TryInvokeAfterDeactivateAsync(
        ResourceActivationLifecycleContext context,
        Exception writeFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await lifecycleHooks.InvokeAfterDeactivateAsync(context with
            {
                OperationStatus = LifecycleOperationStatus.Failed,
                OperationFailure = LifecycleOperationFailures.FromException(writeFailure),
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Validates reference targets (ADR 0002 §6, #87). Strict mode throws; lenient mode logs and
    /// returns the diagnostics so the save hooks receive them as warnings.
    /// </summary>
    private async ValueTask<IReadOnlyList<FacetValueDiagnostic>> ValidateReferencesAsync(
        ResourceDefinition? definition,
        IReadOnlyDictionary<string, object> aspects,
        string resourceId,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        if (definition is null)
            return [];

        var diagnostics = await referenceValidator.ValidateAsync(definition, aspects, tenant, cancellationToken);
        if (diagnostics.Count == 0)
            return [];

        if (referenceValidation == ReferenceValidationMode.Strict)
            throw new FacetValueValidationException(diagnostics);

        foreach (var diagnostic in diagnostics)
            LogReferenceWarning(resourceId, diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId, diagnostic.Message);

        return diagnostics;
    }

    /// <summary>
    /// The aspects an update writes. References in untouched aspects were validated when written, so a
    /// target that later disappears does not block unrelated edits.
    /// </summary>
    internal static IReadOnlyDictionary<string, object> UpdatedAspects(
        IReadOnlyDictionary<string, object> mergedAspects,
        UpdateResourceRequest request) =>
        request.AspectUpdates.Keys
            .Where(mergedAspects.ContainsKey)
            .ToDictionary(static key => key, key => mergedAspects[key], StringComparer.Ordinal);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Saving resource '{ResourceId}' with an invalid reference [{Code}] at {AspectKey}/{FacetDefinitionId}: {Message}")]
    private partial void LogReferenceWarning(string resourceId, string code, string aspectKey, string facetDefinitionId, string message);

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
