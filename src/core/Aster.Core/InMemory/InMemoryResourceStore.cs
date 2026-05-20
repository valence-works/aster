using System.Collections.Concurrent;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory store for <see cref="Resource"/> versions and activation state.
/// Intended for use by <see cref="InMemoryResourceManager"/> only.
/// </summary>
public sealed class InMemoryResourceStore : IResourceVersionReader, IResourceVersionWriter
{
    /// <summary>
    /// Resource version history keyed by <c>ResourceId</c>.
    /// Each list is ordered from V1 to the latest; access must be synchronised via <c>lock</c>.
    /// </summary>
    internal readonly ConcurrentDictionary<string, List<Resource>> Versions =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Activation state keyed by <c>ResourceId</c> → channel name → set of active version numbers.
    /// The inner <see cref="ConcurrentDictionary{TKey,TValue}"/> is used as a lock target for
    /// atomic read-modify-write on the contained <see cref="HashSet{T}"/>.
    /// </summary>
    internal readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HashSet<int>>> Activations =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Last persisted activation state keyed by <c>ResourceId</c> → channel name.
    /// </summary>
    internal readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ActivationState>> ActivationStates =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the ordered version list for a resource, or <see langword="null"/> if it does not exist.
    /// The caller must <c>lock</c> the returned list when reading or mutating.
    /// </summary>
    internal List<Resource>? TryGetVersions(string resourceId) =>
        Versions.TryGetValue(resourceId, out var list) ? list : null;

    /// <summary>
    /// Returns the activation channel map for a resource, creating it if absent.
    /// </summary>
    internal ConcurrentDictionary<string, HashSet<int>> GetOrAddActivations(string resourceId) =>
        Activations.GetOrAdd(resourceId, _ => new ConcurrentDictionary<string, HashSet<int>>(StringComparer.Ordinal));

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
        ResourceVersionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resources = request.Scope switch
        {
            ResourceVersionScope.Latest => ReadLatestVersions(cancellationToken),
            ResourceVersionScope.AllVersions => ReadAllVersions(cancellationToken),
            ResourceVersionScope.Active => ReadActiveVersions(request.ActivationChannel, cancellationToken),
            ResourceVersionScope.Draft => ReadDraftVersions(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unknown resource version scope.")
        };

        return ValueTask.FromResult<IEnumerable<Resource>>(resources.ToList());
    }

    /// <inheritdoc />
    public ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var versions = Versions.GetOrAdd(resource.ResourceId, _ => []);
        lock (versions)
        {
            versions.Add(resource);
        }

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
        cancellationToken.ThrowIfCancellationRequested();

        var channelActivations = GetOrAddActivations(resourceId);
        lock (channelActivations)
            channelActivations[channel] = state.ActiveVersions.ToHashSet();

        var states = ActivationStates.GetOrAdd(
            resourceId,
            _ => new ConcurrentDictionary<string, ActivationState>(StringComparer.Ordinal));
        states[channel] = state;

        return ValueTask.FromResult(state);
    }

    /// <summary>
    /// Returns all resource IDs that belong to the specified definition.
    /// </summary>
    internal IEnumerable<string> GetResourceIdsForDefinition(string definitionId)
    {
        foreach (var (resourceId, list) in Versions)
        {
            lock (list)
            {
                if (list.Count > 0 && string.Equals(list[0].DefinitionId, definitionId, StringComparison.Ordinal))
                    yield return resourceId;
            }
        }
    }

    private IEnumerable<Resource> ReadLatestVersions(CancellationToken cancellationToken)
    {
        foreach (var versionList in Versions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (versionList)
            {
                if (versionList.Count > 0)
                    yield return versionList[^1];
            }
        }
    }

    private IEnumerable<Resource> ReadAllVersions(CancellationToken cancellationToken)
    {
        foreach (var versionList in Versions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<Resource> snapshot;
            lock (versionList)
                snapshot = [.. versionList];

            foreach (var resource in snapshot)
                yield return resource;
        }
    }

    private IEnumerable<Resource> ReadActiveVersions(string? channel, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        foreach (var (resourceId, versionList) in Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Activations.TryGetValue(resourceId, out var channelActivations))
                continue;

            HashSet<int> activeVersionNumbers;
            lock (channelActivations)
            {
                activeVersionNumbers = channelActivations.TryGetValue(channel, out var active)
                    ? new HashSet<int>(active)
                    : [];
            }

            if (activeVersionNumbers.Count == 0)
                continue;

            List<Resource> snapshot;
            lock (versionList)
                snapshot = versionList.Where(r => activeVersionNumbers.Contains(r.Version)).ToList();

            foreach (var resource in snapshot)
                yield return resource;
        }
    }

    private IEnumerable<Resource> ReadDraftVersions(CancellationToken cancellationToken)
    {
        var activeVersionsByResource = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var (resourceId, channelActivations) in Activations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (channelActivations)
            {
                activeVersionsByResource[resourceId] = channelActivations.Values
                    .SelectMany(static versions => versions)
                    .ToHashSet();
            }
        }

        foreach (var (resourceId, versionList) in Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            activeVersionsByResource.TryGetValue(resourceId, out var activeVersionNumbers);

            List<Resource> snapshot;
            lock (versionList)
            {
                snapshot = versionList
                    .Where(r => activeVersionNumbers?.Contains(r.Version) != true)
                    .ToList();
            }

            foreach (var resource in snapshot)
                yield return resource;
        }
    }
}
