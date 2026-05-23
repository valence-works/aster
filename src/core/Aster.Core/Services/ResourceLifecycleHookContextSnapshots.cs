using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;

namespace Aster.Core.Services;

internal static class ResourceLifecycleHookContextSnapshots
{
    public static TContext Snapshot<TContext>(TContext context)
        where TContext : ResourceLifecycleContext =>
        context switch
        {
            ResourceSaveLifecycleContext save => (TContext)(ResourceLifecycleContext)(save with
            {
                Resource = save.Resource is null ? null : Snapshot(save.Resource),
            }),
            ResourceActivationLifecycleContext activation => (TContext)(ResourceLifecycleContext)(activation with
            {
                ActiveVersions = Snapshot(activation.ActiveVersions),
            }),
            ResourceExportLifecycleContext export => (TContext)(ResourceLifecycleContext)(export with
            {
                ExportRequest = Snapshot(export.ExportRequest),
            }),
            ResourceImportLifecycleContext import => (TContext)(ResourceLifecycleContext)(import with
            {
                ImportOptions = Snapshot(import.ImportOptions),
            }),
            _ => context,
        };

    public static Resource Snapshot(Resource resource) =>
        resource with
        {
            Aspects = new ReadOnlyDictionary<string, object>(
                resource.Aspects.ToDictionary(static pair => pair.Key, static pair => SnapshotAspectValue(pair.Value), StringComparer.Ordinal)),
        };

    public static IReadOnlyList<int> Snapshot(IReadOnlyList<int> activeVersions) =>
        new ReadOnlyCollection<int>(activeVersions.ToArray());

    public static PortableSnapshotExportRequest Snapshot(PortableSnapshotExportRequest request) =>
        new()
        {
            ScopeMode = request.ScopeMode,
            DefinitionIds = new HashSet<string>(request.DefinitionIds ?? [], StringComparer.Ordinal),
            ResourceIds = new HashSet<string>(request.ResourceIds ?? [], StringComparer.Ordinal),
            ResourceVersionScope = request.ResourceVersionScope,
            SpecificResourceVersions = new HashSet<ResourceVersionReference>(request.SpecificResourceVersions ?? []),
        };

    public static PortableImportOptions Snapshot(PortableImportOptions options) =>
        new()
        {
            CollisionMode = options.CollisionMode,
        };

    private static object SnapshotAspectValue(object? value) =>
        value switch
        {
            null => null!,
            JsonObject jsonObject => jsonObject.DeepClone(),
            JsonArray jsonArray => jsonArray.DeepClone(),
            JsonValue jsonValue => jsonValue.DeepClone(),
            JsonElement jsonElement => jsonElement.Clone(),
            IReadOnlyDictionary<string, object> dictionary => new ReadOnlyDictionary<string, object>(
                dictionary.ToDictionary(static pair => pair.Key, static pair => SnapshotAspectValue(pair.Value), StringComparer.Ordinal)),
            IDictionary dictionary => SnapshotDictionary(dictionary),
            IList<object> list => new ReadOnlyCollection<object>(list.Select(SnapshotAspectValue).ToArray()),
            IReadOnlyList<object> list => new ReadOnlyCollection<object>(list.Select(SnapshotAspectValue).ToArray()),
            IList list => new ReadOnlyCollection<object>(list.Cast<object?>().Select(SnapshotAspectValue).ToArray()),
            _ => value,
        };

    private static object SnapshotDictionary(IDictionary dictionary)
    {
        var stringKeyed = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not string key)
                return SnapshotObjectDictionary(dictionary);

            stringKeyed[key] = SnapshotAspectValue(entry.Value);
        }

        return new ReadOnlyDictionary<string, object>(stringKeyed);
    }

    private static ReadOnlyDictionary<object, object?> SnapshotObjectDictionary(IDictionary dictionary)
    {
        var copy = new Dictionary<object, object?>();
        foreach (DictionaryEntry entry in dictionary)
            copy[entry.Key] = SnapshotAspectValue(entry.Value);

        return new ReadOnlyDictionary<object, object?>(copy);
    }
}
