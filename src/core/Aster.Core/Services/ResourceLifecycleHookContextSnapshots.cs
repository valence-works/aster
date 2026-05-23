using System.Collections.ObjectModel;
using Aster.Core.Models.Instances;

namespace Aster.Core.Services;

internal static class ResourceLifecycleHookContextSnapshots
{
    public static Resource Snapshot(Resource resource) =>
        resource with
        {
            Aspects = new ReadOnlyDictionary<string, object>(
                resource.Aspects.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
        };

    public static IReadOnlyList<int> Snapshot(IReadOnlyList<int> activeVersions) =>
        new ReadOnlyCollection<int>(activeVersions.ToArray());
}
