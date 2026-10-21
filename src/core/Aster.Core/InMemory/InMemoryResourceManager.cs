using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Microsoft.Extensions.Logging;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IResourceManager"/> and <see cref="IResourceVersionWriter"/>.
/// Provides full create → version → activate lifecycle for resource instances.
/// </summary>
public sealed partial class InMemoryResourceManager : IResourceManager, IResourceVersionWriter
{
    private readonly InMemoryResourceStore store;
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IIdentityGenerator identityGenerator;
    private readonly IResourceReferenceValidator referenceValidator;
    private readonly ReferenceValidationMode referenceValidation;
    private readonly ILogger<InMemoryResourceManager> logger;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryResourceManager"/>.
    /// </summary>
    /// <param name="store">The backing in-memory resource store.</param>
    /// <param name="definitionStore">The definition store (used for singleton enforcement).</param>
    /// <param name="identityGenerator">Generator for new resource and version IDs.</param>
    /// <param name="logger">The logger.</param>
    public InMemoryResourceManager(
        InMemoryResourceStore store,
        IResourceDefinitionStore definitionStore,
        IIdentityGenerator identityGenerator,
        ILogger<InMemoryResourceManager> logger)
        : this(store, definitionStore, identityGenerator, new AsterOptions(), logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryResourceManager"/>.
    /// </summary>
    /// <param name="store">The backing in-memory resource store.</param>
    /// <param name="definitionStore">The definition store (used for singleton enforcement).</param>
    /// <param name="identityGenerator">Generator for new resource and version IDs.</param>
    /// <param name="options">Host options; supplies the reference validation mode.</param>
    /// <param name="logger">The logger.</param>
    public InMemoryResourceManager(
        InMemoryResourceStore store,
        IResourceDefinitionStore definitionStore,
        IIdentityGenerator identityGenerator,
        AsterOptions options,
        ILogger<InMemoryResourceManager> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(identityGenerator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.store = store;
        this.definitionStore = definitionStore;
        this.identityGenerator = identityGenerator;
        referenceValidator = new ResourceReferenceValidator(store);
        referenceValidation = options.ReferenceValidation;
        this.logger = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResourceManager — Create / Update
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask<Resource> CreateAsync(
        string definitionId,
        CreateResourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(request);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);

        // Resolve ResourceId
        var resourceId = string.IsNullOrWhiteSpace(request.ResourceId)
            ? identityGenerator.NewId()
            : request.ResourceId;

        // Singleton enforcement
        var definition = await definitionStore.GetDefinitionAsync(definitionId, tenant, cancellationToken);
        if (definition?.IsSingleton == true)
        {
            var existingIds = store.GetResourceIdsForDefinition(definitionId, tenant);
            if (existingIds.Any())
                throw new SingletonViolationException(definitionId);
        }

        // Facet value validation — must fail before any state change (TryAdd below).
        if (definition is not null)
        {
            var facetValueDiagnostics = FacetValueValidator.Validate(definition, request.InitialAspects);
            if (facetValueDiagnostics.Count > 0)
                throw new FacetValueValidationException(facetValueDiagnostics);
        }

        await ValidateReferencesAsync(definition, request.InitialAspects, resourceId, tenant, cancellationToken);

        // Atomic duplicate ID check - TryAdd is atomic so only one creator can win.
        var versionList = new List<Resource>();
        if (!store.Versions.TryAdd((tenant.TenantId, resourceId), versionList))
            throw new DuplicateResourceIdException(resourceId);

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

        return await SaveVersionAsync(resource, cancellationToken);
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

        var versions = store.TryGetVersions(resourceId, tenant)
            ?? throw new VersionNotFoundException(resourceId, request.BaseVersion);

        Resource latest;
        lock (versions)
        {
            if (versions.Count == 0)
                throw new VersionNotFoundException(resourceId, request.BaseVersion);

            latest = versions[^1];
        }

        if (latest.Version != request.BaseVersion)
            throw new ConcurrencyException(resourceId, request.BaseVersion, latest.Version);

        // Merge aspects — State Replace semantics per updated aspect key
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

        // Facet value validation — must fail before the new version is written.
        var definition = await RecordedDefinitionResolver.ResolveAsync(definitionStore, updated, tenant, cancellationToken);
        if (definition is not null)
        {
            var facetValueDiagnostics = FacetValueValidator.Validate(definition, mergedAspects);
            if (facetValueDiagnostics.Count > 0)
                throw new FacetValueValidationException(facetValueDiagnostics);
        }

        await ValidateReferencesAsync(
            definition, DefaultResourceManager.UpdatedAspects(mergedAspects, request), resourceId, tenant, cancellationToken);

        return await SaveVersionAsync(updated, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResourceManager — Read
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ValueTask<Resource?> GetVersionAsync(string resourceId, int version, CancellationToken cancellationToken = default) =>
        GetVersionAsync(resourceId, version, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public ValueTask<Resource?> GetVersionAsync(
        string resourceId,
        int version,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = store.TryGetVersions(resourceId, tenant);
        if (versions is null)
            return ValueTask.FromResult<Resource?>(null);

        lock (versions)
        {
            var match = versions.FirstOrDefault(r => r.Version == version);
            return ValueTask.FromResult<Resource?>(match);
        }
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> GetVersionsAsync(string resourceId, CancellationToken cancellationToken = default) =>
        GetVersionsAsync(resourceId, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> GetVersionsAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = store.TryGetVersions(resourceId, tenant);
        if (versions is null)
            return ValueTask.FromResult<IEnumerable<Resource>>([]);

        lock (versions)
        {
            return ValueTask.FromResult<IEnumerable<Resource>>([.. versions]);
        }
    }

    /// <inheritdoc />
    public ValueTask<Resource?> GetLatestVersionAsync(string resourceId, CancellationToken cancellationToken = default) =>
        GetLatestVersionAsync(resourceId, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public ValueTask<Resource?> GetLatestVersionAsync(
        string resourceId,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = store.TryGetVersions(resourceId, tenant);
        if (versions is null)
            return ValueTask.FromResult<Resource?>(null);

        lock (versions)
        {
            return ValueTask.FromResult<Resource?>(versions.Count > 0 ? versions[^1] : null);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResourceManager — Activation
    // ──────────────────────────────────────────────────────────────────────────

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

        var versions = store.TryGetVersions(resourceId, tenant);
        if (versions is null)
            throw new VersionNotFoundException(resourceId, version);

        lock (versions)
        {
            var match = versions.FirstOrDefault(r => r.Version == version);
            if (match is null)
                throw new VersionNotFoundException(resourceId, version);
        }

        var channelActivations = store.GetOrAddActivations(resourceId, tenant);
        var newActiveVersions = new HashSet<int>();

        lock (channelActivations)
        {
            if (channelActivations.TryGetValue(channel, out var existing))
            {
                if (allowMultipleActive)
                    newActiveVersions = new HashSet<int>(existing);
            }

            newActiveVersions.Add(version);
            channelActivations[channel] = newActiveVersions;
        }

        var state = new ActivationState
        {
            TenantScope = tenant,
            ResourceId = resourceId,
            Channel = channel,
            ActiveVersions = newActiveVersions.Order().ToList(),
            LastUpdated = DateTime.UtcNow,
        };

        await UpdateActivationAsync(resourceId, channel, state, cancellationToken);
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

        var channelActivations = store.GetOrAddActivations(resourceId, tenant);

        lock (channelActivations)
        {
            if (channelActivations.TryGetValue(channel, out var existing))
            {
                existing.Remove(version);
                channelActivations[channel] = existing;
            }
        }

        var remaining = channelActivations.TryGetValue(channel, out var current) ? current : new HashSet<int>();
        var state = new ActivationState
        {
            TenantScope = tenant,
            ResourceId = resourceId,
            Channel = channel,
            ActiveVersions = [.. remaining],
            LastUpdated = DateTime.UtcNow,
        };

        await UpdateActivationAsync(resourceId, channel, state, cancellationToken);
        LogResourceDeactivated(resourceId, version, channel);
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(
        string resourceId,
        string channel,
        CancellationToken cancellationToken = default) =>
        GetActiveVersionsAsync(resourceId, channel, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> GetActiveVersionsAsync(
        string resourceId,
        string channel,
        TenantScope tenantScope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = store.TryGetVersions(resourceId, tenant);
        if (versions is null)
            return ValueTask.FromResult<IEnumerable<Resource>>([]);

        if (!store.Activations.TryGetValue((tenant.TenantId, resourceId), out var channelActivations))
            return ValueTask.FromResult<IEnumerable<Resource>>([]);

        HashSet<int> activeVersionNumbers;
        lock (channelActivations)
        {
            activeVersionNumbers = channelActivations.TryGetValue(channel, out var set)
                ? new HashSet<int>(set)
                : [];
        }

        List<Resource> result;
        lock (versions)
        {
            result = versions.Where(r => activeVersionNumbers.Contains(r.Version)).ToList();
        }

        return ValueTask.FromResult<IEnumerable<Resource>>(result);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResourceVersionWriter
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var tenant = TenantScopeResolver.Resolve(resource.TenantScope);
        var scopedResource = resource with { TenantScope = tenant };

        var versions = store.Versions.GetOrAdd((tenant.TenantId, scopedResource.ResourceId), _ => []);
        lock (versions)
        {
            versions.Add(scopedResource);
        }

        LogResourceSaved(scopedResource.ResourceId, scopedResource.Version, scopedResource.Id);

        return ValueTask.FromResult(scopedResource);
    }

    /// <inheritdoc />
    public ValueTask<ActivationState> UpdateActivationAsync(
        string resourceId,
        string channel,
        ActivationState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(state);

        return store.UpdateActivationAsync(resourceId, channel, state, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Structured log methods (source generated)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates reference targets (ADR 0002 §6, #87): strict mode throws, lenient mode logs warnings.
    /// </summary>
    private async ValueTask ValidateReferencesAsync(
        ResourceDefinition? definition,
        IReadOnlyDictionary<string, object> aspects,
        string resourceId,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        if (definition is null)
            return;

        var diagnostics = await referenceValidator.ValidateAsync(definition, aspects, tenant, cancellationToken);
        if (diagnostics.Count == 0)
            return;

        if (referenceValidation == ReferenceValidationMode.Strict)
            throw new FacetValueValidationException(diagnostics);

        foreach (var diagnostic in diagnostics)
            LogReferenceWarning(resourceId, diagnostic.Code, diagnostic.AspectKey, diagnostic.FacetDefinitionId, diagnostic.Message);
    }

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
