using System.Text.Json;
using System.Text.Json.Nodes;
using Aster.Core.Models.Definitions;
using static Aster.Core.Services.FacetMetadataValidator;

namespace Aster.Core.Services;

/// <summary>
/// Shared reading and rewriting of <c>reference</c> and <c>reference[]</c> facet values in aspect payloads.
/// </summary>
internal static class ReferenceFacetValues
{
    /// <summary>
    /// The reference facets a definition declares, ordered by aspect key, then facet ID (ordinal).
    /// </summary>
    public static IReadOnlyList<ReferenceFacet> Facets(ResourceDefinition definition)
    {
        var facets = new List<ReferenceFacet>();
        foreach (var (aspectKey, aspect) in definition.AspectDefinitions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var facet in aspect.FacetDefinitions.OrderBy(static facet => facet.FacetDefinitionId, StringComparer.Ordinal))
            {
                var descriptor = ParseFacetType(facet.Type);
                if (descriptor.Element == FacetValueType.Reference)
                    facets.Add(new ReferenceFacet(aspectKey, facet.FacetDefinitionId, descriptor.IsArray, facet.Metadata));
            }
        }

        return facets;
    }

    /// <summary>
    /// Every well-typed reference value in the payloads, in facet order, then element order. Ill-typed
    /// values are skipped; <see cref="FacetValueValidator"/> reports them.
    /// </summary>
    public static IEnumerable<ReferenceValue> Read(IReadOnlyList<ReferenceFacet> facets, IReadOnlyDictionary<string, object> aspects)
    {
        foreach (var facet in facets)
        {
            if (!aspects.TryGetValue(facet.AspectKey, out var payload)
                || payload is null
                || !AspectFacetValueResolver.TryResolveValue(payload, facet.FacetDefinitionId, out var rawValue))
            {
                continue;
            }

            if (!facet.IsArray)
            {
                if (TryNormalizeValue(rawValue, FacetValueType.Reference, out var normalized))
                    yield return new ReferenceValue(facet, null, (string)normalized);

                continue;
            }

            if (!TryGetArrayElements(rawValue, out var elements))
                continue;

            for (var index = 0; index < elements.Count; index++)
            {
                if (TryNormalizeValue(elements[index], FacetValueType.Reference, out var normalized))
                    yield return new ReferenceValue(facet, index, (string)normalized);
            }
        }
    }

    /// <summary>
    /// Returns the aspects with every reference value that <paramref name="map"/> names replaced by its
    /// mapped target. Only payloads that change are rewritten; each becomes a <see cref="JsonElement"/>
    /// serialized with default options, so property names are kept.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Rewrite(
        IReadOnlyList<ReferenceFacet> facets,
        IReadOnlyDictionary<string, object> aspects,
        IReadOnlyDictionary<string, string> map)
    {
        var changes = Read(facets, aspects).Where(value => map.ContainsKey(value.TargetResourceId)).ToList();
        if (changes.Count == 0)
            return aspects;

        var rewritten = new Dictionary<string, object>(aspects, StringComparer.Ordinal);
        foreach (var aspectChanges in changes.GroupBy(static change => change.Facet.AspectKey, StringComparer.Ordinal))
        {
            var payload = JsonSerializer.SerializeToNode(aspects[aspectChanges.Key])!.AsObject();
            foreach (var change in aspectChanges)
            {
                var propertyName = PropertyName(payload, change.Facet.FacetDefinitionId);
                var target = map[change.TargetResourceId];
                if (change.ElementIndex is { } index)
                    payload[propertyName]!.AsArray()[index] = target;
                else
                    payload[propertyName] = target;
            }

            rewritten[aspectChanges.Key] = JsonSerializer.SerializeToElement(payload);
        }

        return rewritten;
    }

    /// <summary>
    /// The payload property that holds a facet, matched like <see cref="AspectFacetValueResolver"/>:
    /// exactly, then ignoring case.
    /// </summary>
    private static string PropertyName(JsonObject payload, string facetDefinitionId)
    {
        if (payload.ContainsKey(facetDefinitionId))
            return facetDefinitionId;

        return payload.Select(static property => property.Key)
            .First(key => string.Equals(key, facetDefinitionId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A declared reference facet.</summary>
    public sealed record ReferenceFacet(string AspectKey, string FacetDefinitionId, bool IsArray, FacetMetadata? Metadata);

    /// <summary>One reference value: a scalar (<see cref="ElementIndex"/> is <see langword="null"/>) or one array element.</summary>
    public sealed record ReferenceValue(ReferenceFacet Facet, int? ElementIndex, string TargetResourceId);
}
