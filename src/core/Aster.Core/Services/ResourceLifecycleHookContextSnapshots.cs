using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Models.Definitions;
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
                Snapshot = export.Snapshot is null ? null : Snapshot(export.Snapshot),
                ExportResult = export.ExportResult is null ? null : Snapshot(export.ExportResult),
            }),
            ResourceImportLifecycleContext import => (TContext)(ResourceLifecycleContext)(import with
            {
                Snapshot = Snapshot(import.Snapshot),
                ImportOptions = Snapshot(import.ImportOptions),
                Preview = import.Preview is null ? null : Snapshot(import.Preview),
                ImportResult = import.ImportResult is null ? null : Snapshot(import.ImportResult),
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

    public static PortableSnapshot Snapshot(PortableSnapshot snapshot) =>
        snapshot with
        {
            Definitions = ReadOnly(snapshot.Definitions.Select(Snapshot)),
            Resources = ReadOnly(snapshot.Resources.Select(Snapshot)),
            ActivationStates = ReadOnly(snapshot.ActivationStates.Select(Snapshot)),
        };

    public static ResourceDefinition Snapshot(ResourceDefinition definition) =>
        definition with
        {
            AspectDefinitions = new ReadOnlyDictionary<string, AspectDefinition>(
                definition.AspectDefinitions.ToDictionary(static pair => pair.Key, static pair => Snapshot(pair.Value), StringComparer.Ordinal)),
        };

    public static AspectDefinition Snapshot(AspectDefinition definition) =>
        definition with
        {
            FacetDefinitions = ReadOnly(definition.FacetDefinitions),
        };

    public static ActivationState Snapshot(ActivationState state) =>
        state with
        {
            ActiveVersions = Snapshot(state.ActiveVersions),
        };

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

    public static PortableSnapshotExportResult Snapshot(PortableSnapshotExportResult result) =>
        result with
        {
            Snapshot = result.Snapshot is null ? null : Snapshot(result.Snapshot),
            Diagnostics = ReadOnly(result.Diagnostics),
            SkippedActivationEntries = ReadOnly(result.SkippedActivationEntries),
        };

    public static PortableImportPreview Snapshot(PortableImportPreview preview) =>
        preview with
        {
            IdentityMap = ReadOnly(preview.IdentityMap),
            Diagnostics = ReadOnly(preview.Diagnostics),
        };

    public static PortableImportResult Snapshot(PortableImportResult result) =>
        result with
        {
            IdentityMap = ReadOnly(result.IdentityMap),
            Diagnostics = ReadOnly(result.Diagnostics),
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

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());
}
