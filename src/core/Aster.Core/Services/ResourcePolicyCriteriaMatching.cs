using Aster.Core.Models.Instances;
using Aster.Core.Models.Policies;

namespace Aster.Core.Services;

/// <summary>
/// The per-resource policy criteria checks shared by policy previews and policy application, so a fresh preview
/// proposes only candidates that application then accepts (#206).
/// </summary>
internal static class ResourcePolicyCriteriaMatching
{
    /// <summary>
    /// Whether <paramref name="resource"/> is at least the criteria's minimum age at <paramref name="timestamp"/>.
    /// Always true when the criteria set no minimum age.
    /// </summary>
    public static bool MatchesMinimumAge(ResourcePolicyCriteria criteria, Resource resource, DateTimeOffset timestamp)
    {
        if (criteria.MinimumAge is not { } minimumAge)
            return true;

        var created = new DateTimeOffset(DateTime.SpecifyKind(resource.Created, DateTimeKind.Utc));
        return timestamp - created >= minimumAge;
    }

    /// <summary>
    /// Whether the resource's lifecycle marker state, <see cref="ResourceLifecycleMarkerState.None"/> when it has no
    /// marker, equals the criteria's lifecycle state. Always true when the criteria set no lifecycle state.
    /// </summary>
    public static bool MatchesLifecycleState(
        ResourcePolicyCriteria criteria,
        string resourceId,
        IReadOnlyDictionary<string, ResourceLifecycleMarker> markers)
    {
        if (criteria.LifecycleState is not { } expected)
            return true;

        var actual = markers.TryGetValue(resourceId, out var marker)
            ? marker.State
            : ResourceLifecycleMarkerState.None;
        return actual == expected;
    }
}
