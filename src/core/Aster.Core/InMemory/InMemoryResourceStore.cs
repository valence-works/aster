using System.Collections.Concurrent;
using Aster.Core.Models.Instances;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory store for <see cref="Resource"/> versions and activation state.
/// Intended for use by <see cref="InMemoryResourceManager"/> only.
/// </summary>
public sealed class InMemoryResourceStore
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
    /// Durable channel mode keyed by <c>ResourceId</c> → channel name → <see cref="ChannelMode"/>.
    /// </summary>
    internal readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ChannelMode>> ChannelModes =
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

    /// <summary>
    /// Returns the channel mode map for a resource, creating it if absent.
    /// </summary>
    internal ConcurrentDictionary<string, ChannelMode> GetOrAddChannelModes(string resourceId) =>
        ChannelModes.GetOrAdd(resourceId, _ => new ConcurrentDictionary<string, ChannelMode>(StringComparer.Ordinal));

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
}
