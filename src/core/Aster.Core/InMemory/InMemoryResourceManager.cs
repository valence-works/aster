using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Microsoft.Extensions.Logging;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IResourceManager"/> and <see cref="IResourceWriteStore"/>.
/// Provides full create → version → activate lifecycle for resource instances.
/// </summary>
public sealed partial class InMemoryResourceManager : IResourceManager, IResourceWriteStore
{
    private readonly InMemoryResourceStore store;
    private readonly IResourceDefinitionStore definitionStore;
    private readonly IIdentityGenerator identityGenerator;
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
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(definitionStore);
        ArgumentNullException.ThrowIfNull(identityGenerator);
        ArgumentNullException.ThrowIfNull(logger);

        this.store = store;
        this.definitionStore = definitionStore;
        this.identityGenerator = identityGenerator;
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

        // Resolve ResourceId
        var resourceId = string.IsNullOrWhiteSpace(request.ResourceId)
            ? identityGenerator.NewId()
            : request.ResourceId;

        // Atomic duplicate ID check — TryAdd is atomic so only one creator can win
        var versionList = new List<Resource>();
        if (!store.Versions.TryAdd(resourceId, versionList))
            throw new DuplicateResourceIdException(resourceId);

        // Singleton enforcement
        var definition = await definitionStore.GetDefinitionAsync(definitionId, cancellationToken);
        if (definition?.IsSingleton == true)
        {
            var existingIds = store.GetResourceIdsForDefinition(definitionId);
            if (existingIds.Any())
                throw new SingletonViolationException(definitionId);
        }

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

        var versions = store.TryGetVersions(resourceId)
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

        return await SaveVersionAsync(updated, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IResourceManager — Read
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ValueTask<Resource?> GetVersionAsync(string resourceId, int version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var versions = store.TryGetVersions(resourceId);
        if (versions is null)
            return ValueTask.FromResult<Resource?>(null);

        lock (versions)
        {
            var match = versions.FirstOrDefault(r => r.Version == version);
            return ValueTask.FromResult<Resource?>(match);
        }
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> GetVersionsAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var versions = store.TryGetVersions(resourceId);
        if (versions is null)
            return ValueTask.FromResult<IEnumerable<Resource>>([]);

        lock (versions)
        {
            return ValueTask.FromResult<IEnumerable<Resource>>([.. versions]);
        }
    }

    /// <inheritdoc />
    public ValueTask<Resource?> GetLatestVersionAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var versions = store.TryGetVersions(resourceId);
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var versions = store.TryGetVersions(resourceId);
        if (versions is null)
            throw new VersionNotFoundException(resourceId, version);

        Resource latest;
        lock (versions)
        {
            var match = versions.FirstOrDefault(r => r.Version == version);
            if (match is null)
                throw new VersionNotFoundException(resourceId, version);

            latest = versions[^1];
        }

        // Optimistic concurrency: version must be the current latest
        if (version != latest.Version)
            throw new ConcurrencyException(resourceId, version, latest.Version);

        var channelActivations = store.GetOrAddActivations(resourceId);
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
            ResourceId = resourceId,
            Channel = channel,
            ActiveVersions = [.. newActiveVersions],
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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var channelActivations = store.GetOrAddActivations(resourceId);

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
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var versions = store.TryGetVersions(resourceId);
        if (versions is null)
            return ValueTask.FromResult<IEnumerable<Resource>>([]);

        if (!store.Activations.TryGetValue(resourceId, out var channelActivations))
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
    // IResourceWriteStore
    // ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var versions = store.Versions.GetOrAdd(resource.ResourceId, _ => []);
        lock (versions)
        {
            versions.Add(resource);
        }

        LogResourceSaved(resource.ResourceId, resource.Version, resource.Id);

        return ValueTask.FromResult(resource);
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

        // Activation state is already updated in memory by ActivateAsync / DeactivateAsync;
        // this method exists as the provider-agnostic persistence hook (no-op for in-memory).
        return ValueTask.FromResult(state);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Structured log methods (source generated)
    // ──────────────────────────────────────────────────────────────────────────

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
